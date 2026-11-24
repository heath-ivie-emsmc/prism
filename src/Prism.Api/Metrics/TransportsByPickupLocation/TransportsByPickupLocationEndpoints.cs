using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports by pickup source and sending facility.</summary>
internal static class TransportsByPickupLocationEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/transports-by-pickup-location", Route)
            .WithName("GetTransportsByPickupLocation")
            .WithSummary("Transports by pickup location")
            .WithDescription(
                "Transports by where the patient was picked up for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, by date of service. The pickup `sources` come from the origin modifier on the claim: `scene` (S), `residence` (R), `custodial-facility` (E), `hospital` (H), `skilled-nursing` (N), `physician-office` (P), `transfer-site` (I), `diagnostic-site` (D), `dialysis` (G and J) and `other` (anything else, blank or not a single letter). "
                + "This mapping is general CMS ambulance billing knowledge, to be confirmed against the warehouse values. Each source has its transports, `share` of all transports (0 to 1) and `changePoints` (percentage points against the month before), loaded miles (statute), the payer mix of its transports and `netCash` (net cash posted in the month by posting month, a different basis from the date-of-service counts). "
                + "`facilities` lists the sending facilities of the facility-based sources (hospital, skilled nursing, physician office, transfer site, diagnostic site, dialysis): the 20 with the most transports across the whole result and one `Other` row (`isOther` true, no `sourceKey`) for the rest. Scene, residence, custodial facility and other pickups are never broken down by name, and no address, city, state or zip is ever returned. "
                + "Facility text comes from the warehouse: it is trimmed, control characters are removed and it is cut to 120 characters. `serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using a `key` from the breakdown route. `division` limits it to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<TransportsByPickupLocationResponse>()
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
        ITransportsByPickupLocationMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(ITransportsByPickupLocationMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new TransportsByPickupLocationResponse(MetricOf(ITransportsByPickupLocationMetric.Definition), request.ProviderId, null, null, ServiceLineName(resolved.ServiceLine), resolved.Group, 0, 0, [], [])
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        TransportsByPickupLocation result = await metric.GetPickupsAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        int total = result.Sources.Sum(s => s.Transports);
        int priorTotal = result.Sources.Sum(s => s.PriorTransports);
        TransportsByPickupLocationResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.PriorPeriod.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            total,
            priorTotal,
            [.. result.Sources.Select(s => new PickupSourceResponse(
                s.Key,
                s.Name,
                s.FacilityBased,
                s.Transports,
                s.PriorTransports,
                s.Transports - s.PriorTransports,
                MetricMath.Share(s.Transports, total),
                MetricMath.Share(s.PriorTransports, priorTotal),
                MetricMath.ChangePoints(s.Transports, total, s.PriorTransports, priorTotal),
                s.LoadedMiles,
                s.PriorLoadedMiles,
                s.NetCash,
                s.PriorNetCash,
                [.. s.Payers.Select(p => new PayerShareResponse(p.Key, p.Name, p.Transports, p.PriorTransports, MetricMath.Share(p.Transports, s.Transports), MetricMath.Share(p.PriorTransports, s.PriorTransports)))]))],
            [.. result.Facilities.Select(f => new PickupFacilityResponse(f.SourceKey, f.Name, f.IsOther, f.Transports, f.PriorTransports, f.Transports - f.PriorTransports, MetricMath.Share(f.Transports, total), f.LoadedMiles))]);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }
}

/// <summary>Transports by pickup source for <c>Period</c> and the month before it, by date of service. <c>Facilities</c> are the sending facilities of the facility-based sources. Never an address.</summary>
public sealed record TransportsByPickupLocationResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    int TotalTransports,
    int PriorTotalTransports,
    IReadOnlyList<PickupSourceResponse> Sources,
    IReadOnlyList<PickupFacilityResponse> Facilities)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// One pickup source. <c>FacilityBased</c> is false for scene, residence, custodial facility and other, which are never listed by name. <c>Share</c> and <c>PriorShare</c> are fractions (0 to 1) of all transports,
/// <c>ChangePoints</c> is the move in percentage points. <c>NetCash</c> is by posting month, a different basis from the date-of-service counts.
/// </summary>
public sealed record PickupSourceResponse(
    string Key,
    string Name,
    bool FacilityBased,
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

/// <summary>A sending facility (warehouse text, cleaned, at most 120 characters), or the one <c>Other</c> row (<c>SourceKey</c> null). <c>Share</c> is a fraction of all transports in <c>Period</c>.</summary>
public sealed record PickupFacilityResponse(string? SourceKey, string Name, bool IsOther, int Transports, int PriorTransports, int ChangeTransports, decimal? Share, decimal LoadedMiles);
