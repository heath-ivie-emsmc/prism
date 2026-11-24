using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Each tail number's contribution to the change in net cash (<see cref="ContributionSql"/>), attributed to the tail of the encounter's trip row, so an encounter with no trip row
/// has no tail and is not counted. The tail text is cleaned in SQL (<see cref="TripSql.CleanTail"/>) and a blank tail is <c>Not recorded</c>. Air only: the service line is
/// already narrowed to air by the route.
/// </summary>
public sealed class CashContributionByAircraftMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ICashContributionByAircraftMetric
{
    internal const string ByAircraftSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, {{TripSql.CleanTail}} AS item
            FROM reporting.trip_detail_snapshot
            WHERE {{ContributionSql.Scope}}
        ),
        {{ContributionSql.Ranking}}
        """;

    public async Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContributionLine> lines = await ContributionSql.ReadAsync(warehouse, providers, ByAircraftSql, "CashContributionByAircraftMetric", new ContributionRequest(providerId, period, serviceLine, groupKey, division), cancellationToken);
        List<ContributionRow> rows = ContributionSql.Order(
            lines.Where(l => !l.Other).Select(l => new ContributionRow(l.Item, AircraftPerformanceMetric.Display(l.Item), false, l.NetCash, l.PriorNetCash)),
            lines.Where(l => l.Other).Select(l => new ContributionRow(null, Contribution.OtherName, true, l.NetCash, l.PriorNetCash)));
        return CashContribution.Of(ICashContributionByAircraftMetric.Definition, period, serviceLine, groupKey, rows);
    }
}
