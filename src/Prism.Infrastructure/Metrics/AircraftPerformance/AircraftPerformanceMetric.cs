using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports, loaded miles and net cash by tail number (the warehouse's <c>vehicle</c>). Transports and miles are by date-of-service month from
/// <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter). Net cash is by posting month from the ledger (<see cref="NetCashSql"/>), attributed to the tail
/// of the encounter's trip row, so an encounter with no trip row has no tail and is not counted. The tail text is cleaned in SQL (control characters and runs of spaces become
/// one space, trimmed, cut to 120 characters, blank becomes <c>Not recorded</c>) and the database keeps the top 100 tails by transports and sums the rest into one row.
/// The Provider filter is in the Catalog. Only counts and sums leave the query.
/// </summary>
public sealed class AircraftPerformanceMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : IAircraftPerformanceMetric
{
    // Row kinds: 1 is a listed tail, 2 the sum of the rest.
    internal const string PerformanceSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, dos, loaded_miles, {{TripSql.CleanTail}} AS tail
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        ),
        flown AS (
            SELECT tail,
                   COUNT(*) FILTER (WHERE to_char(dos, 'YYYYMM') = @period) AS cur_t,
                   COUNT(*) FILTER (WHERE to_char(dos, 'YYYYMM') = @prior) AS prior_t,
                   COALESCE(SUM(loaded_miles) FILTER (WHERE to_char(dos, 'YYYYMM') = @period), 0) AS cur_m,
                   COALESCE(SUM(loaded_miles) FILTER (WHERE to_char(dos, 'YYYYMM') = @prior), 0) AS prior_m
            FROM trips
            WHERE dos >= @from_date AND dos < @to_date
            GROUP BY tail
        ),
        paid AS (
            SELECT t.tail,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @period), 0) AS cur_c,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @prior), 0) AS prior_c
            FROM cash
            JOIN trips t ON t.encounter_id = cash.encounter_id
            GROUP BY t.tail
        ),
        joined AS (
            SELECT COALESCE(f.tail, p.tail) AS tail,
                   COALESCE(f.cur_t, 0) AS cur_t, COALESCE(f.prior_t, 0) AS prior_t,
                   COALESCE(f.cur_m, 0) AS cur_m, COALESCE(f.prior_m, 0) AS prior_m,
                   COALESCE(p.cur_c, 0) AS cur_c, COALESCE(p.prior_c, 0) AS prior_c
            FROM flown f
            FULL JOIN paid p ON p.tail = f.tail
        ),
        ranked AS (
            SELECT *, ROW_NUMBER() OVER (ORDER BY cur_t DESC, prior_t DESC, cur_c DESC, tail) AS rn
            FROM joined
        )
        SELECT 1 AS kind, tail, cur_t::bigint, prior_t::bigint, cur_m, prior_m, cur_c, prior_c FROM ranked WHERE rn <= @top
        UNION ALL
        SELECT 2, NULL, SUM(cur_t)::bigint, SUM(prior_t)::bigint, SUM(cur_m), SUM(prior_m), SUM(cur_c), SUM(prior_c) FROM ranked WHERE rn > @top HAVING COUNT(*) > 0
        """;

    public async Task<AircraftPerformance> GetPerformanceAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod next = period.AddMonths(1);
        var query = new WarehouseQuery(
            PerformanceSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
                ["period"] = period.ToString(),
                ["prior"] = prior.ToString(),
                ["from_date"] = new DateOnly(prior.Year, prior.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
                ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
                [DivisionSql.Parameter] = division?.Name ?? string.Empty,
                ["group_key"] = groupKey ?? string.Empty,
                ["top"] = IAircraftPerformanceMetric.TopTails,
                ["max_name"] = IAircraftPerformanceMetric.MaxNameLength,
            }) { Name = "AircraftPerformanceMetric" };

        IReadOnlyList<(int Kind, string? Tail, PeriodFigures Figures)> rows = await warehouse.QueryAsync(
            query,
            reader => (
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                new PeriodFigures((int)reader.GetInt64(2), (int)reader.GetInt64(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7))),
            cancellationToken);

        List<AircraftRow> aircraft = [.. rows.Where(r => r.Kind == 1).Select(r => new AircraftRow(Display(r.Tail), false, r.Figures))];
        if (rows.FirstOrDefault(r => r.Kind == 2) is { Figures: { } rest })
        {
            aircraft.Add(new AircraftRow(IAircraftPerformanceMetric.OtherName, true, rest));
        }

        PeriodFigures totals = aircraft.Aggregate(PeriodFigures.None, (sum, row) => sum.Plus(row.Figures));
        return new AircraftPerformance(IAircraftPerformanceMetric.Definition, period, prior, serviceLine, groupKey, totals, aircraft);
    }

    // A real tail called "Other" must not look like the roll-up row.
    internal static string Display(string? tail) =>
        string.Equals(tail, IAircraftPerformanceMetric.OtherName, StringComparison.OrdinalIgnoreCase) ? "Other tail" : tail ?? IAircraftPerformanceMetric.NotRecordedName;
}
