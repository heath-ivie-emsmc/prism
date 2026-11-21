using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Net cash collected, from <c>reporting.kpi_net_collections_ap__prov</c>. The SQL is fixed text. Every value is a bound parameter, and the
/// Provider filter is here, in the Catalog, so no endpoint can forget it. Only the current load (<c>is_current</c>) is read: older snapshots are history.
/// A period whose <c>net_collections</c> is NULL has no figure yet (a future month, say), so it is left out rather than shown as zero.
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

    public async Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken)
    {
        var query = new WarehouseQuery(PeriodsSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken) });

        IReadOnlyList<string> rows = await warehouse.QueryAsync(query, reader => reader.GetString(0), cancellationToken);

        // A value that is not a valid period is skipped, not trusted: it would otherwise reach the UI as a drop-down entry.
        return rows.Select(row => ApPeriod.TryParse(row, out ApPeriod period) ? (ApPeriod?)period : null).OfType<ApPeriod>().ToList();
    }

    public async Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(months, INetCollectionsMetric.MaxMonths);

        ApPeriod start = endPeriod.AddMonths(-(months - 1));
        var query = new WarehouseQuery(
            SeriesSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["from_period"] = start.ToString(),
                ["to_period"] = endPeriod.ToString(),
            });

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
        return new NetCollectionsSeries(INetCollectionsMetric.Definition, endPeriod, months, snapshot, points);
    }
}
