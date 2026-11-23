using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports by payer name inside one payer group, from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter), by date-of-service month.
/// It uses the same trips, filters and <see cref="PayerGroupSql"/> as payer mix, so the rows add up to that group's share. The Provider filter is in the Catalog.
/// The database keeps the top payers and sums the rest into one row, so at most <c>TopPayers + 2</c> rows come back whatever the number of payer names.
/// A payer name is cleaned in SQL (control characters and runs of spaces become one space, then it is cut to the length cap) before it is grouped.
/// </summary>
public sealed class PayerMixDetailMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : IPayerMixDetailMetric
{
    // Row kinds: 0 is the group's name (no counts), 1 a top payer, 2 the sum of the rest.
    internal const string PayerMixDetailSql =
        $$"""
        WITH trips AS (
            SELECT level_of_service, prim_pay, prim_pay_category, dos
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
        ),
        tagged AS (
            SELECT to_char(dos, 'YYYYMM') AS ym,
                   lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) AS group_key,
                   MIN(level_of_service) OVER (PARTITION BY lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-'))) AS group_name,
                   {{PayerGroupSql.CaseExpression}} AS payer,
                   COALESCE(NULLIF(left(trim(regexp_replace(COALESCE(prim_pay, ''), '[[:cntrl:][:space:]]+', ' ', 'g')), @max_name), ''), 'Unknown payer') AS payer_name
            FROM trips
            WHERE (@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
               OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing'))
        ),
        in_group AS (
            SELECT ym, group_name, payer, payer_name
            FROM tagged
            WHERE @group_key = '' OR group_key = @group_key
        ),
        counts AS (
            SELECT payer_name,
                   COUNT(*) FILTER (WHERE ym = @current) AS current_count,
                   COUNT(*) FILTER (WHERE ym = @prior) AS prior_count
            FROM in_group
            WHERE payer = @payer_key
            GROUP BY payer_name
        ),
        ranked AS (
            SELECT payer_name, current_count, prior_count,
                   ROW_NUMBER() OVER (ORDER BY current_count + prior_count DESC, payer_name) AS rn
            FROM counts
        )
        SELECT 0 AS kind, MIN(group_name) AS name, 0::bigint AS current_count, 0::bigint AS prior_count
        FROM in_group
        WHERE @group_key <> ''
        HAVING COUNT(*) > 0
        UNION ALL
        SELECT 1, payer_name, current_count, prior_count FROM ranked WHERE rn <= @top
        UNION ALL
        SELECT 2, NULL, SUM(current_count)::bigint, SUM(prior_count)::bigint FROM ranked WHERE rn > @top HAVING COUNT(*) > 0
        """;

    public Task<PayerMixDetail> GetDetailAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, string payerGroupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        (string Key, string Name) payerGroup = PayerMixMetric.Groups.FirstOrDefault(g => g.Key == payerGroupKey);
        if (payerGroup.Key is null || !IPayerMixDetailMetric.IsDrillable(payerGroupKey))
        {
            throw new ArgumentException("The payer group has no payer detail.", nameof(payerGroupKey));
        }

        return RunAsync(providerId, new DetailQuery(period, serviceLine, groupKey, division, payerGroup), cancellationToken);
    }

    private async Task<PayerMixDetail> RunAsync(int providerId, DetailQuery request, CancellationToken cancellationToken)
    {
        (ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, (string Key, string Name) payerGroup) = request;
        string payerGroupKey = payerGroup.Key;
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod next = period.AddMonths(1);
        var query = new WarehouseQuery(
            PayerMixDetailSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["from_date"] = new DateOnly(prior.Year, prior.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
                ["group_key"] = groupKey ?? string.Empty,
                ["payer_key"] = payerGroupKey,
                ["current"] = period.ToString(),
                ["prior"] = prior.ToString(),
                ["top"] = IPayerMixDetailMetric.TopPayers,
                ["max_name"] = IPayerMixDetailMetric.MaxNameLength,
            }) { Name = "PayerMixDetailMetric" };

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3)),
            cancellationToken);

        List<PayerMixDetailPayer> payers = [.. rows.Where(r => r.Kind == 1).Select(r => new PayerMixDetailPayer(Display(r.Name), r.Current, r.Prior))];
        if (rows.FirstOrDefault(r => r.Kind == 2) is { } rest)
        {
            payers.Add(new PayerMixDetailPayer(IPayerMixDetailMetric.OtherName, rest.Current, rest.Prior));
        }

        string? groupName = rows.FirstOrDefault(r => r.Kind == 0)?.Name?.Trim();
        return new PayerMixDetail(IPayerMixDetailMetric.Definition, period, prior, serviceLine, groupKey, groupName, payerGroup.Key, payerGroup.Name, payers);
    }

    // A real payer called "Other" must not look like the roll-up row.
    private static string Display(string? name) =>
        string.Equals(name, IPayerMixDetailMetric.OtherName, StringComparison.OrdinalIgnoreCase) ? "Other payer" : name ?? "Unknown payer";

    /// <summary>The filters of one read: the period, Service Line, optional aircraft type or level of service, optional Reporting Division and the payer group asked about.</summary>
    private readonly record struct DetailQuery(ApPeriod Period, ServiceLineFilter ServiceLine, string? GroupKey, ReportingDivision? Division, (string Key, string Name) PayerGroup);

    private sealed record Row(int Kind, string? Name, int Current, int Prior);
}
