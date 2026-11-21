using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The cash forecast, from <c>reporting.forecast_cash_per_trip</c> (one row per Provider, month and Service Line). The SQL is fixed text, every value is a bound
/// parameter and the Provider filter is here in the Catalog. Only the current load (<c>is_current</c>) is read. Air and ground rows are told apart by <c>is_air</c>.
/// </summary>
public sealed class NetCashProjectionMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : INetCashProjectionMetric
{
    // LEAST and GREATEST ignore NULLs, so a month with only one of the two forecasts still has a range. Months after the chosen period only.
    internal const string ProjectionSql =
        """
        SELECT month_index,
               SUM(total_projected_cash) AS total,
               LEAST(SUM(total_projected_cash), SUM(forecasted_ar_cash), SUM(forecasted_budget_cash)) AS low,
               GREATEST(SUM(total_projected_cash), SUM(forecasted_ar_cash), SUM(forecasted_budget_cash)) AS high,
               MAX(snapshot_date) AS snapshot_date
        FROM reporting.forecast_cash_per_trip
        WHERE provider_id = @provider_id AND is_current AND total_projected_cash IS NOT NULL
          AND month_index >= @first_month
          AND ((@include_air AND is_air) OR (@include_ground AND NOT is_air))
        GROUP BY month_index
        ORDER BY month_index
        LIMIT @month_limit
        """;

    public async Task<NetCashProjection> GetProjectionAsync(int providerId, ApPeriod afterPeriod, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
    {
        ApPeriod first = afterPeriod.AddMonths(1);
        var query = new WarehouseQuery(
            ProjectionSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["first_month"] = new DateOnly(first.Year, first.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                ["month_limit"] = INetCashProjectionMetric.ProjectedMonths,
            });

        IReadOnlyList<(DateOnly Month, decimal Total, decimal Low, decimal High, DateOnly? Snapshot)> rows = await warehouse.QueryAsync(
            query,
            reader => (
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(4))),
            cancellationToken);

        List<NetCashProjectionMonth> months = rows
            .Select(row => new NetCashProjectionMonth(new ApPeriod(row.Month.Year, row.Month.Month), row.Total, row.Low, row.High))
            .ToList();
        return new NetCashProjection(INetCashProjectionMetric.Definition, afterPeriod, serviceLine, rows.Max(row => row.Snapshot), months);
    }
}
