using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Net cash by posting month and level of service. The cash rule is shared with the breakdown (<see cref="NetCashSql"/>) and the group keys and names
/// come from it too, so a group here is the same group there. The Provider filter is in the Catalog: the accounting periods and the trip rows are
/// both the Provider's. Only aggregates leave the query.
/// </summary>
public sealed class NetCashByGroupMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : INetCashByGroupMetric
{
    // Adjustment periods are named "-202608" and belong to the month "202608".
    internal const string NetCashByGroupSql =
        $$"""
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND replace(name, '-', '') = ANY(@periods)
        ),
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, level_of_service
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
        )
        SELECT trips.level_of_service, cash.ym, SUM(cash.amount) AS amount, COUNT(*) AS transports
        FROM cash
        JOIN trips ON trips.encounter_id = cash.encounter_id
        WHERE (@include_air AND COALESCE(trips.level_of_service, '') IN ('Rotor Wing', 'Fixed Wing'))
           OR (@include_ground AND COALESCE(trips.level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing'))
        GROUP BY trips.level_of_service, cash.ym
        """;

    public async Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
    {
        string[] periods = Enumerable.Range(0, INetCashByGroupMetric.MonthsBefore + 1).Select(i => period.AddMonths(-i).ToString()).ToArray();
        var query = new WarehouseQuery(
            NetCashByGroupSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["periods"] = periods,
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
            }) { Name = "NetCashByGroupMetric" };

        // A row whose month does not parse cannot come from the periods asked for, so it is dropped rather than guessed at.
        IReadOnlyList<NetCashByGroupPoint?> rows = await warehouse.QueryAsync(
            query,
            reader =>
            {
                string? levelOfService = reader.IsDBNull(0) ? null : reader.GetString(0);
                string key = NetCashBreakdownMetric.GroupKey(levelOfService);
                return ApPeriod.TryParse(reader.GetString(1), out ApPeriod posting)
                    ? new NetCashByGroupPoint(key, NetCashBreakdownMetric.GroupName(key, levelOfService), NetCashBreakdownMetric.IsAir(key), posting, reader.GetDecimal(2), (int)reader.GetInt64(3))
                    : null;
            },
            cancellationToken);

        // Two level-of-service spellings can share one key, so the points are added up by group and month.
        List<NetCashByGroupPoint> points = rows
            .OfType<NetCashByGroupPoint>()
            .GroupBy(p => (p.GroupKey, p.Period))
            .Select(g => new NetCashByGroupPoint(g.Key.GroupKey, g.First().GroupName, g.First().IsAir, g.Key.Period, g.Sum(p => p.Cash), g.Sum(p => p.Transports)))
            .OrderBy(p => p.GroupKey, StringComparer.Ordinal)
            .ThenBy(p => p.Period.Year)
            .ThenBy(p => p.Period.Month)
            .ToList();

        return new NetCashByGroup(INetCashByGroupMetric.Definition, period, INetCashByGroupMetric.MonthsBefore, serviceLine, points);
    }
}
