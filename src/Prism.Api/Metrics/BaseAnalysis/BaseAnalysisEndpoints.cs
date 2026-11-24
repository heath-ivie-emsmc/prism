using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports, loaded miles and net cash by Reporting Division, with the tail numbers operating from each.</summary>
internal static class BaseAnalysisEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/base-analysis", Route)
            .WithName("GetBaseAnalysis")
            .WithSummary("Figures by Reporting Division")
            .WithDescription(
                "Transports, loaded miles and net cash for each Reporting Division for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, with the change from the month before. "
                + "Transports and loaded miles (statute) are by date of service. `netCash` is net cash posted in the month (payments less refunds by posting month, without the Old AR import) for encounters that have a trip row. "
                + "`netCashPerTransport` is posting-month net cash divided by date-of-service transports: a ratio of two bases, not the expected payment of a transport, and null when there were no transports. "
                + "Each division has its `key` (from the divisions route, null for the Other row, for transports with no division and for a name the directory does not hold), the warehouse's `name` (a legacy `- Old AR` suffix is folded into the current division) and `aircraft`: the tail numbers that flew from it in `period` with their transport counts, the 10 largest plus one `Other` entry. "
                + "The 100 divisions with the most transports are listed and one `Other` row sums the rest (it lists no aircraft). Aggregate counts only. `serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using a `key` from the breakdown route. `division` limits it to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<BaseAnalysisResponse>()
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
        IBaseAnalysisMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(IBaseAnalysisMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new BaseAnalysisResponse(MetricOf(IBaseAnalysisMetric.Definition), request.ProviderId, null, null, ServiceLineName(resolved.ServiceLine), resolved.Group, FigureResponse.Of(PeriodFigures.None), [])
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        BaseAnalysis result = await metric.GetAnalysisAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        BaseAnalysisResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.PriorPeriod.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            FigureResponse.Of(result.Totals),
            [.. result.Divisions.Select(d => new DivisionRowResponse(d.Key, d.Name, d.IsOther, FigureResponse.Of(d.Figures), [.. d.Aircraft.Select(a => new DivisionTailResponse(a.Tail, a.IsOther, a.Transports))]))]);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }
}

/// <summary>Transports, loaded miles and net cash by Reporting Division for <c>Period</c> and the month before it. <c>Totals</c> adds up every row including Other.</summary>
public sealed record BaseAnalysisResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    FigureResponse Totals,
    IReadOnlyList<DivisionRowResponse> Divisions)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>One Reporting Division. <c>Key</c> is null for the Other row, for transports with no division (<c>Name</c> is <c>Not recorded</c>) and for a name the directory does not hold. <c>Aircraft</c> is empty for the Other row.</summary>
public sealed record DivisionRowResponse(string? Key, string Name, bool IsOther, FigureResponse Figures, IReadOnlyList<DivisionTailResponse> Aircraft);

/// <summary>A tail number operating from a division, with its transports in the period (date of service). <c>IsOther</c> marks the entry that sums the division's other tails.</summary>
public sealed record DivisionTailResponse(string Tail, bool IsOther, int Transports);
