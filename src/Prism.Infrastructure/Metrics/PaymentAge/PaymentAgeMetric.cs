using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Net cash by payer group, posting month and payment age. The cash rule is shared with the breakdown (<see cref="NetCashSql"/>) and the payer group
/// with payer mix (<see cref="PayerGroupSql"/>). Transports come from the latest <c>reporting.trip_detail_snapshot</c> load of the Provider, joined on
/// the encounter, and the Provider filter is in the Catalog: the accounting periods and the trip rows are both the Provider's. Only aggregates leave the query.
/// </summary>
public sealed class PaymentAgeMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : IPaymentAgeMetric
{
    internal static readonly IReadOnlyDictionary<string, string> PayerNames = PayerMixMetric.Groups.ToDictionary(g => g.Key, g => g.Name, StringComparer.Ordinal);

    // Adjustment periods are named "-202608" and belong to the month "202608".
    internal const string PaymentAgeSql =
        $$"""
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND replace(name, '-', '') = ANY(@periods)
        ),
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, level_of_service, prim_pay, prim_pay_category, dos
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id AND dos IS NOT NULL
              AND {{DivisionSql.Filter}}
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
        )
        SELECT {{PayerGroupSql.CaseExpression}} AS payer,
               cash.ym,
               LEAST(@max_age, GREATEST(0, (substr(cash.ym, 1, 4)::int * 12 + substr(cash.ym, 5, 2)::int)
                                           - (extract(year FROM dos)::int * 12 + extract(month FROM dos)::int))) AS age,
               SUM(cash.amount) AS amount,
               COUNT(DISTINCT cash.encounter_id) AS transports
        FROM cash
        JOIN trips ON trips.encounter_id = cash.encounter_id
        WHERE ((@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
               OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))
          AND (@group_key = '' OR lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) = @group_key)
        GROUP BY 1, 2, 3
        """;

    public async Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        string group = groupKey?.Trim().ToLowerInvariant() ?? "";
        string[] periods = Enumerable.Range(0, IPaymentAgeMetric.MonthsBefore + 1).Select(i => period.AddMonths(-i).ToString()).ToArray();
        var query = new WarehouseQuery(
            PaymentAgeSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["periods"] = periods,
                ["max_age"] = IPaymentAgeMetric.MaxAgeMonths,
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
                ["group_key"] = group,
            }) { Name = "PaymentAgeMetric" };

        // A row whose month does not parse cannot come from the periods asked for, so it is dropped rather than guessed at.
        IReadOnlyList<PaymentAgeCell?> rows = await warehouse.QueryAsync(
            query,
            reader =>
            {
                string payer = reader.GetString(0);
                return ApPeriod.TryParse(reader.GetString(1), out ApPeriod posting)
                    ? new PaymentAgeCell(payer, PayerNames.GetValueOrDefault(payer, payer), posting, reader.GetInt32(2), reader.GetDecimal(3), (int)reader.GetInt64(4))
                    : null;
            },
            cancellationToken);
        PaymentAgeCell[] cells = rows.OfType<PaymentAgeCell>().ToArray();

        return new PaymentAge(
            IPaymentAgeMetric.Definition,
            period,
            IPaymentAgeMetric.MonthsBefore,
            serviceLine,
            group.Length == 0 ? null : group,
            cells.OrderBy(c => c.PostingPeriod.Year).ThenBy(c => c.PostingPeriod.Month).ThenBy(c => c.PayerKey, StringComparer.Ordinal).ThenBy(c => c.AgeMonths).ToList());
    }
}
