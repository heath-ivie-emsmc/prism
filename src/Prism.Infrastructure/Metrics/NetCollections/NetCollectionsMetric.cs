using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Net cash collected, from <c>reporting.kpi_net_collections_ap__prov</c>. The SQL is fixed text. Every value is a bound parameter, and the
/// Provider filter is here, in the Catalog, so no endpoint can forget it. Only the current load (<c>is_current</c>) is read: older snapshots are history.
/// A period whose <c>net_collections</c> is NULL has no figure yet (a future month, say), so it is left out rather than shown as zero.
/// The KPI table has no Reporting Division or Service Line, so those are added up from the ledger joined to the trip table (<see cref="NetCashSql"/>).
/// </summary>
public sealed class NetCollectionsMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : INetCollectionsMetric
{
    // ap_year_month is a fixed-width char column. The parameters are cast to match it so the column itself is not wrapped in a function,
    // which would stop the database using an index on it.
    internal const string PeriodsSql =
        """
        SELECT DISTINCT trim(ap_year_month) AS period
        FROM reporting.kpi_net_collections_ap__prov
        WHERE provider_id = @provider_id AND is_current AND net_collections IS NOT NULL
        ORDER BY 1 DESC
        LIMIT 240
        """;

    internal const string SeriesSql =
        """
        SELECT trim(ap_year_month) AS period, SUM(net_collections) AS net_collections, MAX(snapshot_date) AS snapshot_date
        FROM reporting.kpi_net_collections_ap__prov
        WHERE provider_id = @provider_id AND is_current AND net_collections IS NOT NULL
          AND ap_year_month BETWEEN @from_period::bpchar AND @to_period::bpchar
        GROUP BY trim(ap_year_month)
        ORDER BY 1
        """;

    // The same cash rule as NetCashBreakdownMetric.BreakdownSql (payments and refunds, by posting month, adjustment periods folded in, no old import,
    // one transaction counted once), for every month in the range, kept to air or ground by the encounter's level of service.
    internal const string LedgerSeriesSql =
        """
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND replace(name, '-', '') BETWEEN @from_period AND @to_period
        ),
        cash AS (
            SELECT et.encounter_id, p.ym, SUM(et.balance_amount) / 100.0 AS amount
            FROM ods.encounter_transaction et
            JOIN periods p ON p.id = et.accounting_period_id
            WHERE et.type = 'Credit' AND et.user_source IS DISTINCT FROM 'Old AR Import'
              AND EXISTS (
                  SELECT 1
                  FROM ods.encounter_transaction_credit_attribute a
                  JOIN ods.credit c ON c.id = a.credit_id
                  WHERE a.encounter_transaction_id = et.id AND c.credit_type_id IN (2, 5))
            GROUP BY et.encounter_id, p.ym
        ),
        trips AS (
            SELECT encounter_id, level_of_service, snapshot_date
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
        )
        SELECT cash.ym, SUM(cash.amount) AS net_collections, (SELECT MAX(snapshot_date) FROM trips) AS snapshot_date
        FROM cash
        LEFT JOIN trips t ON t.encounter_id = cash.encounter_id
        WHERE (@include_air AND COALESCE(t.level_of_service, '') IN ('Rotor Wing', 'Fixed Wing'))
           OR (@include_ground AND COALESCE(t.level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing'))
        GROUP BY cash.ym
        ORDER BY 1
        """;

    // One Reporting Division: the same net cash rule as net cash by group (NetCashSql), for the encounters whose trip row (latest load) is in the division.
    // The KPI table has no division, so this is the only honest source. An INNER join: cash for an encounter with no trip row, or whose trip has no division,
    // belongs to no division and is left out, so the divisions add up to the headline less that cash. The trips CTE comes first so the ledger read is limited to its encounters.
    internal const string DivisionSeriesSql =
        $$"""
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND replace(name, '-', '') BETWEEN @from_period AND @to_period
        ),
        trips AS (
            SELECT encounter_id, level_of_service, snapshot_date
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
              AND {{DivisionSql.NormalizedName}} = @division
        ),
        {{NetCashSql.CashCteForTrips}}
        SELECT cash.ym, SUM(cash.amount) AS net_collections, (SELECT MAX(snapshot_date) FROM trips) AS snapshot_date
        FROM cash
        JOIN trips t ON t.encounter_id = cash.encounter_id
        WHERE (@include_air AND COALESCE(t.level_of_service, '') IN ('Rotor Wing', 'Fixed Wing'))
           OR (@include_ground AND COALESCE(t.level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing'))
        GROUP BY cash.ym
        ORDER BY 1
        """;

    public async Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken)
    {
        var query = new WarehouseQuery(PeriodsSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken) }) { Name = "NetCollectionsMetric" };

        IReadOnlyList<string> rows = await warehouse.QueryAsync(query, reader => reader.GetString(0), cancellationToken);

        // A value that is not a valid period is skipped, not trusted: it would otherwise reach the UI as a drop-down entry.
        return rows.Select(row => ApPeriod.TryParse(row, out ApPeriod period) ? (ApPeriod?)period : null).OfType<ApPeriod>().ToList();
    }

    public async Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(months, INetCollectionsMetric.MaxMonths);

        ApPeriod start = endPeriod.AddMonths(-(months - 1));
        var parameters = new Dictionary<string, object?>
        {
            [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
            ["from_period"] = start.ToString(),
            ["to_period"] = endPeriod.ToString(),
        };
        string sql = SeriesSql;
        if (division is not null)
        {
            sql = DivisionSeriesSql;
            parameters[DivisionSql.Parameter] = division.Name;
            parameters["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air;
            parameters["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground;
        }
        else if (serviceLine is not ServiceLineFilter.Both)
        {
            sql = LedgerSeriesSql;
            parameters["include_air"] = serviceLine is ServiceLineFilter.Air;
            parameters["include_ground"] = serviceLine is ServiceLineFilter.Ground;
        }

        var query = new WarehouseQuery(sql, parameters) { Name = "NetCollectionsMetric#2" };

        IReadOnlyList<(string Period, decimal Value, DateOnly? Snapshot)> rows = await warehouse.QueryAsync(
            query,
            reader => (
                reader.GetString(0),
                reader.GetDecimal(1),
                reader.IsDBNull(2) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(2))),
            cancellationToken);

        List<NetCollectionsPoint> points = rows
            .Select(row => ApPeriod.TryParse(row.Period, out ApPeriod period) ? new NetCollectionsPoint(period, row.Value) : null)
            .OfType<NetCollectionsPoint>()
            .ToList();
        DateOnly? snapshot = rows.Max(row => row.Snapshot);
        return new NetCollectionsSeries(INetCollectionsMetric.Definition, endPeriod, months, snapshot, points, serviceLine);
    }
}
