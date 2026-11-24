using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports, loaded miles and net cash by tail number.</summary>
internal static class AircraftPerformanceEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/aircraft-performance", Route)
            .WithName("GetAircraftPerformance")
            .WithSummary("Performance by tail number")
            .WithDescription(
                "Transports, loaded miles and net cash for each tail number (the warehouse's vehicle) for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, with the change from the month before. "
                + "Transports and loaded miles (statute) are by date of service. `netCash` is net cash posted in the month (payments less refunds by posting month, without the Old AR import) for encounters that have a trip row. "
                + "`netCashPerTransport` is posting-month net cash divided by date-of-service transports: a ratio of two bases, not the expected payment of a transport, and null when there were no transports. "
                + "The 100 tail numbers with the most transports are listed and one `Other` row (`isOther` true) sums the rest. A transport with no tail is under `Not recorded`. Tail text comes from the warehouse: it is trimmed, control characters are removed and it is cut to 120 characters. "
                + "This Metric is air only: `serviceLine` `air` (default) and `all` both mean air, and `ground` is a 400. `group` narrows it to one aircraft type, using a `key` from the breakdown route. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<AircraftPerformanceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static async Task<IResult> Route(
        [AsParameters] ScopeRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IAircraftPerformanceMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(IAircraftPerformanceMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new AircraftPerformanceResponse(MetricOf(IAircraftPerformanceMetric.Definition), request.ProviderId, null, null, ServiceLineName(resolved.ServiceLine), resolved.Group, FigureResponse.Of(PeriodFigures.None), [])
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        AircraftPerformance result = await metric.GetPerformanceAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        AircraftPerformanceResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.PriorPeriod.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            FigureResponse.Of(result.Totals),
            [.. result.Aircraft.Select(a => new AircraftRowResponse(a.Tail, a.IsOther, FigureResponse.Of(a.Figures)))]);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }
}

/// <summary>Transports, loaded miles and net cash by tail number for <c>Period</c> and the month before it. <c>Totals</c> adds up every row including Other.</summary>
public sealed record AircraftPerformanceResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    FigureResponse Totals,
    IReadOnlyList<AircraftRowResponse> Aircraft)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>One tail number. <c>Tail</c> is warehouse text (cleaned, at most 120 characters), <c>Not recorded</c> or <c>Other</c> when <c>IsOther</c> is true.</summary>
public sealed record AircraftRowResponse(string Tail, bool IsOther, FigureResponse Figures);

/// <summary>
/// Transports and loaded miles by date of service and net cash by posting month, in the month and the month before, with the changes. <c>ChangePercent</c> values are percent units
/// (one decimal), null when the month before had none. <c>NetCashPerTransport</c> is posting-month cash divided by transports, null when there were no transports. It is not an expected payment.
/// </summary>
public sealed record FigureResponse(
    int Transports,
    int PriorTransports,
    int ChangeTransports,
    decimal? ChangeTransportsPercent,
    decimal LoadedMiles,
    decimal PriorLoadedMiles,
    decimal ChangeLoadedMiles,
    decimal? ChangeLoadedMilesPercent,
    decimal NetCash,
    decimal PriorNetCash,
    decimal ChangeNetCash,
    decimal? ChangeNetCashPercent,
    decimal? NetCashPerTransport,
    decimal? PriorNetCashPerTransport)
{
    internal static FigureResponse Of(PeriodFigures f) => new(
        f.Transports,
        f.PriorTransports,
        f.ChangeTransports,
        MetricMath.ChangePercent(f.Transports, f.PriorTransports),
        f.LoadedMiles,
        f.PriorLoadedMiles,
        f.LoadedMiles - f.PriorLoadedMiles,
        MetricMath.ChangePercent(f.LoadedMiles, f.PriorLoadedMiles),
        f.NetCash,
        f.PriorNetCash,
        f.NetCash - f.PriorNetCash,
        MetricMath.ChangePercent(f.NetCash, f.PriorNetCash),
        f.NetCashPerTransport is { } c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null,
        f.PriorNetCashPerTransport is { } p ? Math.Round(p, 2, MidpointRounding.AwayFromZero) : null);
}
