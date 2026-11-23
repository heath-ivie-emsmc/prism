using System.Globalization;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Net cash for one accounting period (and the one before) from the transaction ledger, split by the encounter's level of service, vehicle and
/// reporting division from <c>reporting.trip_detail_snapshot</c>. The cash rule is the one behind <c>kpi_net_collections_ap__prov</c>: credit
/// transactions whose credit is a payment or refund (types 2 and 5), by the accounting period they posted in, without <c>Old AR Import</c>.
/// A transaction counts once, however many credit attributes it has. The Provider filter is in the Catalog: the accounting periods are the Provider's,
/// and the trip rows are joined on the Provider as well. Only patient-free aggregates leave the query.
/// </summary>
public sealed class NetCashBreakdownMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : INetCashBreakdownMetric
{
    // Adjustment periods are named "-202608" and belong to the month "202608". The base is the reporting division as the warehouse names it, without a
    // "- Old AR ..." suffix, so a legacy division is counted under its current one.
    internal const string BreakdownSql =
        $$"""
        WITH periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND name IN (@period, @prior, '-' || @period, '-' || @prior)
        ),
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, level_of_service, vehicle, response_type, loaded_miles,
                   {{DivisionSql.NormalizedName}} AS base
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
        )
        SELECT t.level_of_service, t.vehicle, t.base, cash.ym,
               SUM(cash.amount) AS amount,
               COUNT(*) AS transports,
               COALESCE(SUM(t.loaded_miles), 0) AS loaded_miles,
               COUNT(*) FILTER (WHERE CASE WHEN t.level_of_service IN ('Rotor Wing', 'Fixed Wing')
                                           THEN t.response_type ILIKE 'Scene%' ELSE t.response_type = 'Emergency' END) AS primary_mix,
               COUNT(*) FILTER (WHERE CASE WHEN t.level_of_service IN ('Rotor Wing', 'Fixed Wing')
                                           THEN t.response_type ILIKE 'Interfacility%' ELSE t.response_type = 'Non-Emergency' END) AS secondary_mix
        FROM cash
        LEFT JOIN trips t ON t.encounter_id = cash.encounter_id
        WHERE ((@include_air AND COALESCE(t.level_of_service, '') IN ('Rotor Wing', 'Fixed Wing'))
               OR (@include_ground AND COALESCE(t.level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))
          AND (@division = '' OR t.base = @division)
        GROUP BY t.level_of_service, t.vehicle, t.base, cash.ym
        """;

    // The adjustment period of a month ("-202608") is open or closed on its own, so the month is open if either is.
    internal const string PeriodOpenSql =
        """
        SELECT COALESCE(bool_or(is_open), false)
        FROM ods.accounting_period
        WHERE provider_id = @provider_id AND name IN (@period, '-' || @period)
        """;

    public async Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
    {
        ApPeriod prior = period.AddMonths(-1);
        var query = new WarehouseQuery(
            BreakdownSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["period"] = period.ToString(),
                ["prior"] = prior.ToString(),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
            }) { Name = "NetCashBreakdownMetric" };

        IReadOnlyList<Slice> slices = await warehouse.QueryAsync(
            query,
            reader => new Slice(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3).Trim(),
                new BreakdownFigures(reader.GetDecimal(4), (int)reader.GetInt64(5), reader.GetDecimal(6), (int)reader.GetInt64(7), (int)reader.GetInt64(8))),
            cancellationToken);

        var openQuery = new WarehouseQuery(
            PeriodOpenSql,
            new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = query.Parameters[WarehouseQuery.ProviderParameter], ["period"] = period.ToString() }) { Name = "NetCashBreakdownMetric#2" };
        IReadOnlyList<bool> openRows = await warehouse.QueryAsync(openQuery, reader => reader.GetBoolean(0), cancellationToken);
        bool periodOpen = openRows.Count > 0 && openRows[0];

        string current = period.ToString();
        static BreakdownFigures Pick(IEnumerable<Slice> rows, string ym) => rows.Where(s => s.Period == ym).Aggregate(BreakdownFigures.None, (sum, s) => sum.Plus(s.Figures));

        // A group or a vehicle that only had cash in the prior month still shows, with nothing for this month.
        List<NetCashBreakdownGroup> groups = slices
            .GroupBy(s => GroupKey(s.LevelOfService))
            .Select(g => new NetCashBreakdownGroup(g.Key, GroupName(g.Key, g.First().LevelOfService), IsAir(g.Key), Pick(g, current), Pick(g, prior.ToString())))
            .OrderByDescending(g => g.Current.Cash)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();

        List<NetCashBreakdownRow> rows = slices
            .GroupBy(s => (Group: GroupKey(s.LevelOfService), s.Base, Vehicle: s.Vehicle ?? "Not recorded"))
            .Select(g => new NetCashBreakdownRow(g.Key.Group, GroupName(g.Key.Group, g.First().LevelOfService), IsAir(g.Key.Group), g.Key.Base, g.Key.Vehicle, Pick(g, current), Pick(g, prior.ToString())))
            .OrderByDescending(r => r.Current.Cash)
            .ThenBy(r => r.Vehicle, StringComparer.Ordinal)
            .ToList();

        return new NetCashBreakdown(INetCashBreakdownMetric.Definition, period, prior, serviceLine, periodOpen, groups, rows);
    }

    private sealed record Slice(string? LevelOfService, string? Vehicle, string? Base, string Period, BreakdownFigures Figures);

    internal static string GroupKey(string? levelOfService) =>
        string.IsNullOrWhiteSpace(levelOfService) ? "not-recorded" : levelOfService.Trim().ToLower(CultureInfo.InvariantCulture).Replace(' ', '-');

    internal static bool IsAir(string key) => key is "rotor-wing" or "fixed-wing";

    internal static string GroupName(string key, string? raw) => key switch
    {
        "rotor-wing" => "Rotor wing",
        "fixed-wing" => "Fixed wing",
        "als" => "Advanced life support",
        "als2" => "Advanced life support, level 2",
        "bls" => "Basic life support",
        "sct" => "Specialty care",
        "wheelchair" => "Wheelchair",
        "fire-services" => "Fire services",
        "not-recorded" => "Not recorded",
        _ => raw?.Trim() ?? "Other",
    };
}
