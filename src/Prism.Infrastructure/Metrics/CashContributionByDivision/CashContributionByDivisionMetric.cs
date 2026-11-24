using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Each Reporting Division's contribution to the change in net cash (<see cref="ContributionSql"/>). The division is the warehouse's name with the legacy "- Old AR" suffix folded away
/// (<see cref="DivisionSql.NormalizedName"/>), matched to the Provider's own division keys from the directory. A trip with no division is listed as <c>Not recorded</c>.
/// </summary>
public sealed class CashContributionByDivisionMetric(IWarehouse warehouse, IWarehouseProviderResolver providers, IProviderDivisionDirectory directory) : ICashContributionByDivisionMetric
{
    internal const string ByDivisionSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, COALESCE({{DivisionSql.NormalizedName}}, '') AS item
            FROM reporting.trip_detail_snapshot
            WHERE {{ContributionSql.Scope}}
        ),
        {{ContributionSql.Ranking}}
        """;

    public async Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContributionLine> lines = await ContributionSql.ReadAsync(warehouse, providers, ByDivisionSql, "CashContributionByDivisionMetric", new ContributionRequest(providerId, period, serviceLine, groupKey, division), cancellationToken);
        Dictionary<string, string> keys = (await directory.GetReportingDivisionsAsync(providerId, cancellationToken)).ToDictionary(d => d.Name, d => d.Key, StringComparer.Ordinal);
        List<ContributionRow> rows = ContributionSql.Order(
            lines.Where(l => !l.Other).Select(l => DivisionOf(l, keys)),
            lines.Where(l => l.Other).Select(l => new ContributionRow(null, Contribution.OtherName, true, l.NetCash, l.PriorNetCash)));
        return CashContribution.Of(ICashContributionByDivisionMetric.Definition, period, serviceLine, groupKey, rows);
    }

    private static ContributionRow DivisionOf(ContributionLine line, Dictionary<string, string> keys) =>
        string.IsNullOrEmpty(line.Item)
            ? new ContributionRow(null, IAircraftPerformanceMetric.NotRecordedName, false, line.NetCash, line.PriorNetCash)
            : new ContributionRow(keys.GetValueOrDefault(line.Item), line.Item, false, line.NetCash, line.PriorNetCash);
}
