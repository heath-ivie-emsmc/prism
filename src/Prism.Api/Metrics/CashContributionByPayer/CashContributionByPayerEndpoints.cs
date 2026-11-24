using System.Security.Claims;
using Prism.Application.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Cash change by payer group</summary>
internal static class CashContributionByPayerEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/cash-contribution/payer", Route)
            .WithName("GetCashContributionByPayer")
            .WithSummary("Cash change by payer group")
            .WithDescription(
                "How much each payer group added to (positive `change`) or took from (negative) the change in net cash for `period` (`yyyyMM`, default the newest period with net cash data) against the month before. "
                + "Net cash is posted cash: payments less refunds by posting month, without the Old AR import, for encounters that have a trip row. `total` is the net cash of the scope and the `rows`, the `Other` row included, add up exactly to it, so the contributions always add up to `total.change`. "
                + "Each row is a payer group by the primary payer of the transport the cash was posted against: `commercial`, `medicare`, `medicaid`, `other-gov`, `va`, `facility`, `patient` or `not-recorded`. No payer name is returned. `serviceLine` is `all`, `air` (default) or `ground`. "
                + "`shareOfChange` is a row's change divided by the total change, null when the total did not change, and can be above 1 or below 0 when contributors offset each other. A payer group that has detail carries a `drill` link to its payer detail. "
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
        ICashContributionByPayerMetric metric,
        CancellationToken cancellationToken) =>
        CashContributionRoute.RunAsync(
            ICashContributionByPayerMetric.Definition,
            request,
            user,
            access,
            periods,
            (resolved, period, ct) => metric.GetContributionAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, ct),
            cancellationToken);
}
