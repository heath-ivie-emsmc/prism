using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Loaded miles by tail number, with transports, the average per transport and a monthly trend.</summary>
internal static class LoadedMilesByAircraftEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/loaded-miles-by-aircraft", Route)
            .WithName("GetLoadedMilesByAircraft")
            .WithSummary("Loaded miles by tail number")
            .WithDescription(
                "Loaded miles (statute miles billed with the patient on board), transports and the average loaded miles per transport for each tail number (the warehouse's vehicle) in `period` (`yyyyMM`, default the newest period with net cash data), by date of service. "
                + "`averageLoadedMiles` is null when the tail had no transports in the month. `trend` has one point per month for the last `months` months (6, the default, or 12) ending with `period`, with transports, loaded miles and the average (null for a month with no transports). "
                + "Tails are ranked by their loaded miles in `period`. The 100 largest are listed and one `Other` row (`isOther` true) sums the rest, including its trend. A transport with no tail is under `Not recorded`. Tail text comes from the warehouse: it is trimmed, control characters are removed and it is cut to 120 characters. "
                + "This Metric is air only: `serviceLine` `air` (default) and `all` both mean air, and `ground` is a 400. `group` narrows it to one aircraft type, using a `key` from the breakdown route. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<LoadedMilesByAircraftResponse>()
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
        ILoadedMilesByAircraftMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(ILoadedMilesByAircraftMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new LoadedMilesByAircraftResponse(MetricOf(ILoadedMilesByAircraftMetric.Definition), request.ProviderId, null, resolved.Months, ServiceLineName(resolved.ServiceLine), resolved.Group, [])
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        LoadedMilesByAircraft result = await metric.GetMilesAsync(resolved.Id, period, resolved.Months, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        LoadedMilesByAircraftResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.Months,
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            [.. result.Aircraft.Select(a => new AircraftMilesRowResponse(
                a.Tail,
                a.IsOther,
                a.Transports,
                a.LoadedMiles,
                Average(a.AverageLoadedMiles),
                [.. a.Trend.Select(p => new AircraftMilesPointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.Transports, p.LoadedMiles, Average(p.AverageLoadedMiles)))]))]);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }

    private static decimal? Average(decimal? value) => value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;
}

/// <summary>Loaded miles by tail number for <c>Period</c>, with a monthly trend over <c>Months</c> months (6 or 12).</summary>
public sealed record LoadedMilesByAircraftResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    int Months,
    string ServiceLine,
    string? Group,
    IReadOnlyList<AircraftMilesRowResponse> Aircraft)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// One tail number. <c>Transports</c>, <c>LoadedMiles</c> and <c>AverageLoadedMiles</c> (one decimal, null with no transports) are for <c>Period</c>. <c>Trend</c> has one point per month of the window,
/// oldest first, with zeros for a month the tail did not fly.
/// </summary>
public sealed record AircraftMilesRowResponse(string Tail, bool IsOther, int Transports, decimal LoadedMiles, decimal? AverageLoadedMiles, IReadOnlyList<AircraftMilesPointResponse> Trend);

/// <summary>One month of a tail's trend. <c>AverageLoadedMiles</c> is null when there were no transports.</summary>
public sealed record AircraftMilesPointResponse(string Period, string Label, int Year, int Transports, decimal LoadedMiles, decimal? AverageLoadedMiles);
