using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Matured net cash per transport by payer group. The cash rule is shared with the breakdown (<see cref="NetCashSql"/>) and the payer group with payer mix
/// (<see cref="PayerGroupSql"/>). The ledger is read only for the cohorts' encounters, because the periods reach back to the beginning and the whole ledger is too much to add up. Every transport in the cohorts counts, paid or not, so the average is not flattered by leaving unpaid claims out.
/// The Provider filter is in the Catalog: the accounting periods and the trip rows are both the Provider's. Only aggregates leave the query.
/// </summary>
public sealed class CashPerTransportMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ICashPerTransportMetric
{
    // Adjustment periods are named "-202608" and belong to the month "202608". The months compare as text because they are fixed-width yyyyMM.
    internal const string CashPerTransportSql =
        $$"""
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND replace(name, '-', '') <= @period
        ),
        trips AS (
            SELECT encounter_id, level_of_service, prim_pay, prim_pay_category
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
              AND ((@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
                   OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))
              AND (@group_key = '' OR lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) = @group_key)
        ),
        {{NetCashSql.CashCteForTrips}},
        paid AS (
            SELECT encounter_id, SUM(amount) AS amount FROM cash GROUP BY encounter_id
        )
        SELECT {{PayerGroupSql.CaseExpression}} AS payer,
               COUNT(*) AS transports,
               COALESCE(SUM(paid.amount), 0) AS amount
        FROM trips
        LEFT JOIN paid ON paid.encounter_id = trips.encounter_id
        GROUP BY 1
        """;

    public async Task<CashPerTransport> GetCashPerTransportAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        string group = groupKey?.Trim().ToLowerInvariant() ?? "";
        ApPeriod from = period.AddMonths(-(ICashPerTransportMetric.MaturityMonths + ICashPerTransportMetric.CohortMonths - 1));
        ApPeriod through = period.AddMonths(-ICashPerTransportMetric.MaturityMonths);
        ApPeriod after = through.AddMonths(1);
        var query = new WarehouseQuery(
            CashPerTransportSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["period"] = period.ToString(),
                ["from_date"] = new DateOnly(from.Year, from.Month, 1),
                ["to_date"] = new DateOnly(after.Year, after.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
                ["group_key"] = group,
            }) { Name = "CashPerTransportMetric" };

        IReadOnlyList<(string Payer, int Transports, decimal Cash)> rows = await warehouse.QueryAsync(
            query,
            reader => (reader.GetString(0), (int)reader.GetInt64(1), reader.GetDecimal(2)),
            cancellationToken);

        // Every payer group is listed in a fixed order, with zeros, so a caller never has to guess at a missing one.
        List<PayerCashPerTransport> payers = PayerMixMetric.Groups
            .Where(g => g.Key != "not-recorded" || rows.Any(r => r.Payer == g.Key))
            .Select(g => rows.FirstOrDefault(r => r.Payer == g.Key) is { Payer: not null } row
                ? new PayerCashPerTransport(g.Key, g.Name, row.Transports, row.Cash)
                : new PayerCashPerTransport(g.Key, g.Name, 0, 0m))
            .ToList();

        return new CashPerTransport(ICashPerTransportMetric.Definition, period, from, through, serviceLine, group.Length == 0 ? null : group, payers);
    }
}
