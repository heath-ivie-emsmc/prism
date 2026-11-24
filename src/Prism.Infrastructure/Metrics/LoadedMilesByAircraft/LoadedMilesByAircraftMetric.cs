using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Loaded miles and transports by tail number and date-of-service month from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter), over a window of
/// 6 or 12 months. The database ranks the tails by the miles of the chosen month (then the miles of the window) and returns the monthly rows of the top 100, with the rest
/// summed into one Other row, so the result is at most 101 tails times 12 months. Tail text is cleaned in SQL. The Provider filter is in the Catalog. Only counts and sums leave the query.
/// </summary>
public sealed class LoadedMilesByAircraftMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ILoadedMilesByAircraftMetric
{
    // Row kinds: 1 a listed tail (rank in the second column), 2 the sum of the rest.
    internal const string MilesSql =
        $$"""
        WITH trips AS (
            SELECT dos, loaded_miles, {{TripSql.CleanTail}} AS tail
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        ),
        monthly AS (
            SELECT tail, to_char(dos, 'YYYYMM') AS ym, COUNT(*) AS n, COALESCE(SUM(loaded_miles), 0) AS miles
            FROM trips
            GROUP BY tail, ym
        ),
        ranked AS (
            SELECT tail,
                   ROW_NUMBER() OVER (ORDER BY COALESCE(SUM(miles) FILTER (WHERE ym = @period), 0) DESC, SUM(miles) DESC, tail) AS rn
            FROM monthly
            GROUP BY tail
        )
        SELECT 1 AS kind, r.rn::bigint AS rn, m.tail, m.ym, m.n::bigint AS n, m.miles
        FROM monthly m
        JOIN ranked r ON r.tail = m.tail
        WHERE r.rn <= @top
        UNION ALL
        SELECT 2, 0::bigint, NULL, m.ym, SUM(m.n)::bigint, SUM(m.miles)
        FROM monthly m
        JOIN ranked r ON r.tail = m.tail
        WHERE r.rn > @top
        GROUP BY m.ym
        """;

    public async Task<LoadedMilesByAircraft> GetMilesAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        int count = TrendMonths.IsAllowed(months) ? months : TrendMonths.Default;
        ApPeriod first = period.AddMonths(-(count - 1));
        ApPeriod next = period.AddMonths(1);
        var query = new WarehouseQuery(
            MilesSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["period"] = period.ToString(),
                ["from_date"] = new DateOnly(first.Year, first.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
                ["group_key"] = groupKey ?? string.Empty,
                ["top"] = ILoadedMilesByAircraftMetric.TopTails,
                ["max_name"] = IAircraftPerformanceMetric.MaxNameLength,
            }) { Name = "LoadedMilesByAircraftMetric" };

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(reader.GetInt32(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3).Trim(), (int)reader.GetInt64(4), reader.GetDecimal(5)),
            cancellationToken);

        ApPeriod[] window = [.. Enumerable.Range(0, count).Select(i => period.AddMonths(-(count - 1 - i)))];
        string current = period.ToString();
        List<AircraftMilesRow> aircraft = [.. rows
            .Where(r => r.Kind == 1)
            .GroupBy(r => (r.Rank, r.Tail))
            .OrderBy(g => g.Key.Rank)
            .Select(g => Build(AircraftPerformanceMetric.Display(g.Key.Tail), false, g, window, current))];
        if (rows.Any(r => r.Kind == 2))
        {
            aircraft.Add(Build(IAircraftPerformanceMetric.OtherName, true, rows.Where(r => r.Kind == 2), window, current));
        }

        return new LoadedMilesByAircraft(ILoadedMilesByAircraftMetric.Definition, period, count, serviceLine, groupKey, aircraft);
    }

    private static AircraftMilesRow Build(string tail, bool isOther, IEnumerable<Row> rows, ApPeriod[] window, string current)
    {
        Row[] own = [.. rows];
        List<AircraftMilesPoint> trend = [.. window.Select(p => new AircraftMilesPoint(
            p,
            own.Where(r => r.Month == p.ToString()).Sum(r => r.Transports),
            own.Where(r => r.Month == p.ToString()).Sum(r => r.Miles)))];
        AircraftMilesPoint now = trend.First(p => p.Period.ToString() == current);
        return new AircraftMilesRow(tail, isOther, now.Transports, now.LoadedMiles, trend);
    }

    private sealed record Row(int Kind, long Rank, string? Tail, string Month, int Transports, decimal Miles);
}
