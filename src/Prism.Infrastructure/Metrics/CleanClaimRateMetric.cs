using System.Globalization;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Clean claim counts by month of service from <c>reporting.kpi_clean_claim_rate__prov</c>, latest load for the Provider. A month that the table
/// splits by pod is summed over its pods, unless the table also has a row for the whole Provider, which is used instead so nothing counts twice.
/// The Provider filter is in the Catalog. Only counts leave the query.
/// </summary>
public sealed class CleanClaimRateMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ICleanClaimRateMetric
{
    internal const string RateSql =
        """
        SELECT dos_year_month, pod_id, snapshot_date, clean_claims, rejected_claims, total_claims
        FROM reporting.kpi_clean_claim_rate__prov
        WHERE provider_id = @provider_id
          AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.kpi_clean_claim_rate__prov WHERE provider_id = @provider_id)
          AND dos_year_month <= @through AND dos_year_month >= @since
        """;

    public async Task<CleanClaimRate> GetRateAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken)
    {
        int count = Math.Clamp(months, 1, ICleanClaimRateMetric.MaxMonths);
        ApPeriod first = endPeriod.AddMonths(-(count - 1));
        var query = new WarehouseQuery(
            RateSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["through"] = Format(endPeriod),
                ["since"] = Format(first),
            });

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(
                reader.GetString(0).Trim(),
                !reader.IsDBNull(1),
                reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                reader.IsDBNull(5) ? 0 : reader.GetInt32(5)),
            cancellationToken);

        List<CleanClaimPoint> points = [];
        foreach (IGrouping<string, Row> month in rows.GroupBy(r => r.Month).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!TryParse(month.Key, out ApPeriod period))
            {
                continue;
            }

            // A row without a pod is the whole Provider. Otherwise the pods add up to it.
            List<Row> use = month.Any(r => !r.HasPod) ? month.Where(r => !r.HasPod).ToList() : [.. month];
            points.Add(new CleanClaimPoint(period, use.Sum(r => r.Clean), use.Sum(r => r.Rejected), use.Sum(r => r.Total)));
        }

        return new CleanClaimRate(ICleanClaimRateMetric.Definition, endPeriod, count, rows.Select(r => r.Snapshot).FirstOrDefault(d => d is not null), points);
    }

    /// <summary>The table writes a month as "2026-03".</summary>
    internal static string Format(ApPeriod period) => string.Create(CultureInfo.InvariantCulture, $"{period.Year:D4}-{period.Month:D2}");

    private static bool TryParse(string month, out ApPeriod period) =>
        ApPeriod.TryParse(month.Replace("-", string.Empty, StringComparison.Ordinal), out period);

    private sealed record Row(string Month, bool HasPod, DateOnly? Snapshot, int Clean, int Rejected, int Total);
}
