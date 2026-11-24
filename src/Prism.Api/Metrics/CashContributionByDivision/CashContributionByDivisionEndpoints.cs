using System.Security.Claims;
using Prism.Application.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Cash change by Reporting Division</summary>
internal static class CashContributionByDivisionEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/cash-contribution/division", Route)
            .WithName("GetCashContributionByDivision")
            .WithSummary("Cash change by Reporting Division")
            .WithDescription(
                "How much each Reporting Division added to (positive `change`) or took from (negative) the change in net cash for `period` (`yyyyMM`, default the newest period with net cash data) against the month before. "
                + "Net cash is posted cash: payments less refunds by posting month, without the Old AR import, for encounters that have a trip row. `total` is the net cash of the scope and the `rows`, the `Other` row included, add up exactly to it, so the contributions always add up to `total.change`. "
                + "Each row is a Reporting Division: `key` is from the divisions route (null for the Other row, for transports with no division, whose `name` is `Not recorded`, and for a name the directory does not hold) and `name` is the warehouse's name with a legacy `- Old AR` suffix folded into the current division. The 25 divisions that moved cash most are listed and one `Other` row sums the rest. `serviceLine` is `all`, `air` (default) or `ground`. "
                + "`shareOfChange` is a row's change divided by the total change, null when the total did not change, and can be above 1 or below 0 when contributors offset each other. "
                + "`group` narrows it to one aircraft type or level of service, using a `key` from the breakdown route. `division` limits it to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. "
                + "Aggregates only: no patient or claim detail. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<CashContributionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static Task<IResult> Route(
        [AsParameters] ScopeRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ICashContributionByDivisionMetric metric,
        CancellationToken cancellationToken) =>
        CashContributionRoute.RunAsync(
            ICashContributionByDivisionMetric.Definition,
            request,
            user,
            access,
            periods,
            (resolved, period, ct) => metric.GetContributionAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, ct),
            cancellationToken);
}
