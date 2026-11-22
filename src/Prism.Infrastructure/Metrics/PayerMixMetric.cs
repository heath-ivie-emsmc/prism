using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports by primary payer group from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter), by date-of-service month.
/// The Provider filter is in the Catalog. Only counts leave the query: no patient, claim or payer names.
/// </summary>
public sealed class PayerMixMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : IPayerMixMetric
{
    internal static readonly (string Key, string Name)[] Groups =
    [
        ("commercial", "Commercial"),
        ("medicare", "Medicare"),
        ("medicaid", "Medicaid"),
        ("other-gov", "Other government"),
        ("va", "VA"),
        ("facility", "Facility"),
        ("patient", "Patient responsibility"),
        ("not-recorded", "Not recorded"),
    ];

    // The payer name decides VA first: the warehouse files VA and TRICARE payers under Insurance and Facility as often as under Other Govt. Payers.
    internal const string PayerMixSql =
        $$"""
        WITH trips AS (
            SELECT level_of_service, prim_pay, prim_pay_category, dos
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
              AND dos >= @from_date AND dos < @to_date
        ),
        tagged AS (
            SELECT to_char(dos, 'YYYYMM') AS ym,
                   lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) AS group_key,
                   MIN(level_of_service) OVER (PARTITION BY lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-'))) AS group_name,
                   {{PayerGroupSql.CaseExpression}} AS payer
            FROM trips
            WHERE (@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
               OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing'))
        )
        SELECT ym, payer, MIN(group_name), COUNT(*)
        FROM tagged
        WHERE @group_key = '' OR group_key = @group_key
        GROUP BY ym, payer
        """;

    public async Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod next = period.AddMonths(1);
        var query = new WarehouseQuery(
            PayerMixSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["from_date"] = new DateOnly(prior.Year, prior.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                ["group_key"] = groupKey ?? string.Empty,
            }) { Name = "PayerMixMetric" };

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(reader.GetString(0).Trim(), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), (int)reader.GetInt64(3)),
            cancellationToken);

        string current = period.ToString();
        string previous = prior.ToString();
        List<PayerMixShare> shares = Groups
            .Select(g => new PayerMixShare(
                g.Key,
                g.Name,
                rows.Where(r => r.Payer == g.Key && r.Period == current).Sum(r => r.Count),
                rows.Where(r => r.Payer == g.Key && r.Period == previous).Sum(r => r.Count)))
            .Where(s => s.Key != "not-recorded" || s.Current + s.Prior > 0)
            .ToList();

        string? groupName = rows.Select(r => r.GroupName).FirstOrDefault(n => n is not null)?.Trim();
        return new PayerMix(IPayerMixMetric.Definition, period, prior, serviceLine, groupKey, groupName, shares);
    }

    private sealed record Row(string Period, string Payer, string? GroupName, int Count);
}
