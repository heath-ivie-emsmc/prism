using System.Globalization;
using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports by call type, with payer mix, net cash and a monthly trend.</summary>
internal static class TransportMixByCallTypeEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/transport-mix-by-call-type", Route)
            .WithName("GetTransportMixByCallType")
            .WithSummary("Transports by call type")
            .WithDescription(
                "Transports by call type for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, by date of service. A call type is `scene` or `interfacility` for air and `emergency` or `non-emergency` for ground, from the response type of the transport, and `not-recorded` when the warehouse has none. "
                + "Each call type has its transports, `share` of all transports (0 to 1) and `changePoints` (percentage points against the month before), loaded miles (statute), the payer mix of its transports (`payerMix`, share by payer group of that call type) and `netCash`. "
                + "`netCash` is net cash posted in the month (payments less refunds by posting month, without the Old AR import) for the encounters of that call type. It is on a different basis from the transport counts, which are by date of service, so do not divide one by the other as an expected payment. "
                + "Expected payment per transport is not available in the warehouse and is not estimated. `trend` has one point per month for the last `months` months (6, the default, or 12) ending with `period`, with the transports and share of each call type. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using a `key` from the breakdown route. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<TransportMixByCallTypeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static async Task<IResult> Route(
        [AsParameters] TrendRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ITransportMixByCallTypeMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(ITransportMixByCallTypeMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new TransportMixByCallTypeResponse(MetricOf(ITransportMixByCallTypeMetric.Definition), request.ProviderId, null, null, resolved.Months, ServiceLineName(resolved.ServiceLine), resolved.Group, 0, 0, [], [])
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        TransportMixByCallType mix = await metric.GetMixAsync(resolved.Id, period, resolved.Months, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        int total = mix.CallTypes.Sum(c => c.Transports);
        int priorTotal = mix.CallTypes.Sum(c => c.PriorTransports);
        Dictionary<string, string> names = ITransportMixByCallTypeMetric.CallTypes.ToDictionary(t => t.Key, t => t.Name, StringComparer.Ordinal);
        TransportMixByCallTypeResponse response = new(
            MetricOf(mix.Metric),
            request.ProviderId,
            mix.Period.ToString(),
            mix.PriorPeriod.ToString(),
            mix.Months,
            ServiceLineName(mix.ServiceLine),
            mix.GroupKey,
            total,
            priorTotal,
            [.. mix.CallTypes.Select(c => new CallTypeResponse(
                c.Key,
                c.Name,
                c.Transports,
                c.PriorTransports,
                c.Transports - c.PriorTransports,
                MetricMath.Share(c.Transports, total),
                MetricMath.Share(c.PriorTransports, priorTotal),
                MetricMath.ChangePoints(c.Transports, total, c.PriorTransports, priorTotal),
                c.LoadedMiles,
                c.PriorLoadedMiles,
                c.NetCash,
                c.PriorNetCash,
                [.. c.Payers.Select(p => new PayerShareResponse(p.Key, p.Name, p.Transports, p.PriorTransports, MetricMath.Share(p.Transports, c.Transports), MetricMath.Share(p.PriorTransports, c.PriorTransports)))]))],
            [.. mix.Trend.Select(t =>
            {
                int month = t.Counts.Sum(c => c.Transports);
                return new CallTypeTrendPointResponse(
                    t.Period.ToString(),
                    Months[t.Period.Month - 1],
                    t.Period.Year,
                    month,
                    [.. t.Counts.Select(c => new CallTypeTrendShareResponse(c.Key, names[c.Key], c.Transports, MetricMath.Share(c.Transports, month)))]);
            })]);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }
}

/// <summary>
/// Transports by call type for <c>Period</c> and the month before it, by date of service, and a monthly <c>Trend</c> over <c>Months</c> months (6 or 12).
/// <c>TotalTransports</c> and <c>PriorTotalTransports</c> are the sums of the call types.
/// </summary>
public sealed record TransportMixByCallTypeResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    int Months,
    string ServiceLine,
    string? Group,
    int TotalTransports,
    int PriorTotalTransports,
    IReadOnlyList<CallTypeResponse> CallTypes,
    IReadOnlyList<CallTypeTrendPointResponse> Trend)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// One call type. <c>Share</c> and <c>PriorShare</c> are fractions (0 to 1, four decimals) of all transports, null when there were none. <c>ChangePoints</c> is the move of the share in percentage points
/// (one decimal), null when either month had none. <c>NetCash</c> and <c>PriorNetCash</c> are by posting month, a different basis from the date-of-service counts.
/// </summary>
public sealed record CallTypeResponse(
    string Key,
    string Name,
    int Transports,
    int PriorTransports,
    int ChangeTransports,
    decimal? Share,
    decimal? PriorShare,
    decimal? ChangePoints,
    decimal LoadedMiles,
    decimal PriorLoadedMiles,
    decimal NetCash,
    decimal PriorNetCash,
    IReadOnlyList<PayerShareResponse> PayerMix);

/// <summary>A payer group's transports inside one slice, and its share of that slice (0 to 1, four decimals, null when the slice had none) in the month and the month before.</summary>
public sealed record PayerShareResponse(string Key, string Name, int Transports, int PriorTransports, decimal? Share, decimal? PriorShare);

/// <summary>One month of the trend: all transports and the transports and share of each call type.</summary>
public sealed record CallTypeTrendPointResponse(string Period, string Label, int Year, int Transports, IReadOnlyList<CallTypeTrendShareResponse> CallTypes);

/// <summary>A call type in one month of the trend. <c>Share</c> is a fraction (0 to 1) of that month's transports, null when there were none.</summary>
public sealed record CallTypeTrendShareResponse(string Key, string Name, int Transports, decimal? Share);
