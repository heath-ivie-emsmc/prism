using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports and loaded miles by date-of-service month from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter).
/// The Provider filter is in the Catalog. Only counts and sums leave the query: no patient, claim or payer values.
/// </summary>
public sealed class TransportVolumeMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ITransportVolumeMetric
{
    internal const string VolumeSql =
        $$"""
        SELECT to_char(dos, 'YYYYMM') AS ym, level_of_service, COUNT(*) AS transports, COALESCE(SUM(loaded_miles), 0) AS loaded_miles
        FROM reporting.trip_detail_snapshot
        WHERE provider_id = @provider_id
          AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
          AND dos >= @from_date AND dos < @to_date
          AND {{DivisionSql.Filter}}
          AND ((@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
            OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))
        GROUP BY ym, level_of_service
        """;

    public async Task<TransportVolume> GetVolumeAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
    {
        int count = Math.Clamp(months, 1, ITransportVolumeMetric.MaxMonths);
        ApPeriod first = endPeriod.AddMonths(-(count - 1));
        // The query reaches one month before the series when needed, so the groups can be compared with the month before the last.
        ApPeriod queryFrom = count == 1 ? endPeriod.AddMonths(-1) : first;
        ApPeriod next = endPeriod.AddMonths(1);
        int warehouseId = await providers.ResolveAsync(providerId, cancellationToken);
        var query = new WarehouseQuery(
            VolumeSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = warehouseId,
                ["from_date"] = new DateOnly(queryFrom.Year, queryFrom.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
            }) { Name = "TransportVolumeMetric" };

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(reader.GetString(0).Trim(), reader.IsDBNull(1) ? null : reader.GetString(1), (int)reader.GetInt64(2), reader.GetDecimal(3)),
            cancellationToken);

        string firstText = first.ToString();
        List<TransportVolumePoint> points = rows
            .Where(r => count > 1 || r.Period == firstText)
            .GroupBy(r => r.Period)
            .Select(g => new TransportVolumePoint(ApPeriod.TryParse(g.Key, out ApPeriod p) ? p : endPeriod, g.Sum(r => r.Transports), g.Sum(r => r.LoadedMiles)))
            .OrderBy(p => (p.Period.Year * 12) + p.Period.Month)
            .ToList();

        string last = endPeriod.ToString();
        string prior = endPeriod.AddMonths(-1).ToString();
        Dictionary<string, int> priorByGroup = rows
            .Where(r => r.Period == prior)
            .GroupBy(r => NetCashBreakdownMetric.GroupKey(r.LevelOfService))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Transports), StringComparer.Ordinal);
        List<TransportVolumeGroup> groups = rows
            .Where(r => r.Period == last)
            .GroupBy(r => NetCashBreakdownMetric.GroupKey(r.LevelOfService))
            .Select(g => new TransportVolumeGroup(g.Key, NetCashBreakdownMetric.GroupName(g.Key, g.First().LevelOfService), NetCashBreakdownMetric.IsAir(g.Key), g.Sum(r => r.Transports), priorByGroup.GetValueOrDefault(g.Key)))
            .OrderByDescending(g => g.Transports)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();

        var openQuery = new WarehouseQuery(
            NetCashBreakdownMetric.PeriodOpenSql,
            new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = warehouseId, ["period"] = last }) { Name = "TransportVolumeMetric#2" };
        IReadOnlyList<bool> open = await warehouse.QueryAsync(openQuery, reader => reader.GetBoolean(0), cancellationToken);

        return new TransportVolume(ITransportVolumeMetric.Definition, endPeriod, count, serviceLine, open.Count > 0 && open[0], points, groups);
    }

    private sealed record Row(string Period, string? LevelOfService, int Transports, decimal LoadedMiles);
}
