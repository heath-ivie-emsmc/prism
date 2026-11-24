using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Each payer group's contribution to the change in net cash (<see cref="ContributionSql"/>), by the primary payer group of the encounter's trip row, with the grouping every payer
/// Metric shares (<see cref="PayerGroupSql"/>). Only the eight group keys leave the query: no payer name, patient or claim detail.
/// </summary>
public sealed class CashContributionByPayerMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ICashContributionByPayerMetric
{
    internal const string ByPayerSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, {{PayerGroupSql.CaseExpression}} AS item
            FROM reporting.trip_detail_snapshot
            WHERE {{ContributionSql.Scope}}
        ),
        {{ContributionSql.Ranking}}
        """;

    public async Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContributionLine> lines = await ContributionSql.ReadAsync(warehouse, providers, ByPayerSql, "CashContributionByPayerMetric", new ContributionRequest(providerId, period, serviceLine, groupKey, division), cancellationToken);
        Dictionary<string, string> names = PayerMixMetric.Groups.ToDictionary(g => g.Key, g => g.Name, StringComparer.Ordinal);
        List<ContributionRow> rows = ContributionSql.Order(
            lines.Where(l => !l.Other).Select(l => PayerOf(l, names)),
            lines.Where(l => l.Other).Select(l => new ContributionRow(null, Contribution.OtherName, true, l.NetCash, l.PriorNetCash)));
        return CashContribution.Of(ICashContributionByPayerMetric.Definition, period, serviceLine, groupKey, rows);
    }

    // The CASE only ever yields one of the eight keys, so an unknown key cannot happen. If one did, it is shown as Not recorded, never as its raw text.
    private static ContributionRow PayerOf(ContributionLine line, Dictionary<string, string> names) =>
        line.Item is { } key && names.TryGetValue(key, out string? name)
            ? new ContributionRow(key, name, false, line.NetCash, line.PriorNetCash)
            : new ContributionRow("not-recorded", "Not recorded", false, line.NetCash, line.PriorNetCash);
}
