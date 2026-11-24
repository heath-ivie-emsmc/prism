using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>
/// The one request sequence the three contribution routes share: the checked request (<see cref="MetricRequestResolver"/>), the read through the Metric, and the response. Each route only
/// supplies its catalog entry and how to read its Metric.
/// </summary>
internal static class CashContributionRoute
{
    internal delegate Task<CashContribution> Read(ResolvedMetric request, ApPeriod period, CancellationToken cancellationToken);

    internal static async Task<IResult> RunAsync(
        MetricDefinition definition,
        ScopeRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        Read read,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(Envelope(new CashContributionResponse(MetricOf(definition), request.ProviderId, null, null, ServiceLineName(resolved.ServiceLine), resolved.Group, ContributionTotalResponse.Of(0m, 0m), []), resolved));
        }

        CashContribution result = await read(resolved, period, cancellationToken);
        var links = new DrillLinkBuilder(entry, user, request.ProviderId, new DrillContext(result.Period.ToString(), result.ServiceLine, resolved.Group, resolved.Division?.Key));
        CashContributionResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.PriorPeriod.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            ContributionTotalResponse.Of(result.NetCash, result.PriorNetCash),
            [.. result.Rows.Select(r => ContributionRowResponse.Of(r, result.Change, r.Key is { } key ? links.LinksFor(key, r.Name) : []))]);
        return Results.Ok(Envelope(response, resolved));
    }

    private static CashContributionResponse Envelope(CashContributionResponse response, ResolvedMetric resolved) =>
        response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name };
}

/// <summary>
/// How much each contributor added to or took from the change in net cash (USD) for <c>Period</c> against <c>PriorPeriod</c>. <c>Total</c> is net cash for the scope and the rows, the
/// <c>Other</c> row included, add up exactly to its <c>netCash</c>, <c>priorNetCash</c> and <c>change</c>.
/// </summary>
public sealed record CashContributionResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    ContributionTotalResponse Total,
    IReadOnlyList<ContributionRowResponse> Rows)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>Net cash in the month and the month before and the change between them, with the change as percent units (one decimal, null when the month before had no cash).</summary>
public sealed record ContributionTotalResponse(decimal NetCash, decimal PriorNetCash, decimal Change, decimal? ChangePercent)
{
    internal static ContributionTotalResponse Of(decimal netCash, decimal priorNetCash) =>
        new(netCash, priorNetCash, netCash - priorNetCash, MetricMath.ChangePercent(netCash, priorNetCash));
}

/// <summary>
/// One contributor. <c>Change</c> is its net cash minus its net cash the month before: positive added to the total change, negative took from it. <c>ShareOfChange</c> is
/// <c>change</c> divided by the total change (four decimals), null when the total did not change. Shares can be above 1 or below 0 when contributors offset each other.
/// <c>Key</c> is null for the Other row, for a division with no key in the directory and for transports with no division. <c>Links</c> are the drills the server issues.
/// </summary>
public sealed record ContributionRowResponse(string? Key, string Name, bool IsOther, decimal NetCash, decimal PriorNetCash, decimal Change, decimal? ShareOfChange, IReadOnlyList<LinkResponse> Links)
{
    internal static ContributionRowResponse Of(ContributionRow row, decimal totalChange, IReadOnlyList<LinkResponse> links) =>
        new(row.Key, row.Name, row.IsOther, row.NetCash, row.PriorNetCash, row.Change, MetricMath.Share(row.Change, totalChange), links);
}
