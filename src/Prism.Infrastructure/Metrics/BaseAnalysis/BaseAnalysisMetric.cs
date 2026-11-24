using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Figures by Reporting Division from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter): transports and miles by date-of-service month, net cash by posting
/// month from the ledger (<see cref="NetCashSql"/>) for encounters that have a trip row, and the tail numbers operating from each division in the chosen month. The division is the
/// warehouse's name with the legacy "- Old AR" suffix folded away (<see cref="DivisionSql.NormalizedName"/>), matched to the Provider's own division keys from the directory.
/// The database keeps the top 100 divisions and, within each, the top 10 tails, and sums the rest, so the result is bounded whatever the data. Tail text is cleaned in SQL.
/// The Provider filter is in the Catalog. Only counts and sums leave the query.
/// </summary>
public sealed class BaseAnalysisMetric(IWarehouse warehouse, IWarehouseProviderResolver providers, IProviderDivisionDirectory directory) : IBaseAnalysisMetric
{
    // Row kinds: 1 a listed division, 2 the sum of the other divisions, 3 a tail inside a listed division, 4 the sum of that division's other tails.
    internal const string BaseAnalysisSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, dos, loaded_miles,
                   COALESCE({{DivisionSql.NormalizedName}}, '') AS division,
                   {{TripSql.CleanTail}} AS tail
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        ),
        flown AS (
            SELECT division,
                   COUNT(*) FILTER (WHERE to_char(dos, 'YYYYMM') = @period) AS cur_t,
                   COUNT(*) FILTER (WHERE to_char(dos, 'YYYYMM') = @prior) AS prior_t,
                   COALESCE(SUM(loaded_miles) FILTER (WHERE to_char(dos, 'YYYYMM') = @period), 0) AS cur_m,
                   COALESCE(SUM(loaded_miles) FILTER (WHERE to_char(dos, 'YYYYMM') = @prior), 0) AS prior_m
            FROM trips
            WHERE dos >= @from_date AND dos < @to_date
            GROUP BY division
        ),
        paid AS (
            SELECT t.division,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @period), 0) AS cur_c,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @prior), 0) AS prior_c
            FROM cash
            JOIN trips t ON t.encounter_id = cash.encounter_id
            GROUP BY t.division
        ),
        joined AS (
            SELECT COALESCE(f.division, p.division) AS division,
                   COALESCE(f.cur_t, 0) AS cur_t, COALESCE(f.prior_t, 0) AS prior_t,
                   COALESCE(f.cur_m, 0) AS cur_m, COALESCE(f.prior_m, 0) AS prior_m,
                   COALESCE(p.cur_c, 0) AS cur_c, COALESCE(p.prior_c, 0) AS prior_c
            FROM flown f
            FULL JOIN paid p ON p.division = f.division
        ),
        ranked AS (
            SELECT *, ROW_NUMBER() OVER (ORDER BY cur_t DESC, prior_t DESC, cur_c DESC, division) AS rn
            FROM joined
        ),
        tails AS (
            SELECT division, tail, COUNT(*) AS n
            FROM trips
            WHERE dos >= @from_date AND dos < @to_date AND to_char(dos, 'YYYYMM') = @period
            GROUP BY division, tail
        ),
        tails_ranked AS (
            SELECT t.division, t.tail, t.n, ROW_NUMBER() OVER (PARTITION BY t.division ORDER BY t.n DESC, t.tail) AS trn
            FROM tails t
            JOIN ranked r ON r.division = t.division AND r.rn <= @top
        )
        SELECT 1 AS kind, division, NULL::text AS tail, cur_t::bigint AS cur_t, prior_t::bigint AS prior_t, cur_m, prior_m, cur_c, prior_c FROM ranked WHERE rn <= @top
        UNION ALL
        SELECT 2, NULL, NULL, SUM(cur_t)::bigint, SUM(prior_t)::bigint, SUM(cur_m), SUM(prior_m), SUM(cur_c), SUM(prior_c) FROM ranked WHERE rn > @top HAVING COUNT(*) > 0
        UNION ALL
        SELECT 3, division, tail, n, 0::bigint, 0, 0, 0, 0 FROM tails_ranked WHERE trn <= @top_tails
        UNION ALL
        SELECT 4, division, NULL, SUM(n)::bigint, 0::bigint, 0, 0, 0, 0 FROM tails_ranked WHERE trn > @top_tails GROUP BY division
        """;

    public async Task<BaseAnalysis> GetAnalysisAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod next = period.AddMonths(1);
        var query = new WarehouseQuery(
            BaseAnalysisSql,
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
                ["top"] = IBaseAnalysisMetric.TopDivisions,
                ["top_tails"] = IBaseAnalysisMetric.TopTailsPerDivision,
                ["max_name"] = IAircraftPerformanceMetric.MaxNameLength,
            }) { Name = "BaseAnalysisMetric" };

        IReadOnlyList<Row> rows = await warehouse.QueryAsync(
            query,
            reader => new Row(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                new PeriodFigures((int)reader.GetInt64(3), (int)reader.GetInt64(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8))),
            cancellationToken);

        Dictionary<string, string> keys = (await directory.GetReportingDivisionsAsync(providerId, cancellationToken))
            .ToDictionary(d => d.Name, d => d.Key, StringComparer.Ordinal);

        List<DivisionRow> divisions = [.. rows.Where(r => r.Kind == 1).Select(r => DivisionOf(r, rows, keys))];
        if (rows.FirstOrDefault(r => r.Kind == 2) is { } rest)
        {
            divisions.Add(new DivisionRow(null, "Other", true, rest.Figures, []));
        }

        PeriodFigures totals = divisions.Aggregate(PeriodFigures.None, (sum, row) => sum.Plus(row.Figures));
        return new BaseAnalysis(IBaseAnalysisMetric.Definition, period, prior, serviceLine, groupKey, totals, divisions);
    }

    private static DivisionRow DivisionOf(Row row, IReadOnlyList<Row> all, Dictionary<string, string> keys)
    {
        string name = row.Division ?? string.Empty;
        List<DivisionTail> tails = [.. all.Where(r => r.Kind == 3 && r.Division == name).Select(r => new DivisionTail(AircraftPerformanceMetric.Display(r.Tail), false, r.Figures.Transports))];
        if (all.FirstOrDefault(r => r.Kind == 4 && r.Division == name) is { } rest)
        {
            tails.Add(new DivisionTail(IAircraftPerformanceMetric.OtherName, true, rest.Figures.Transports));
        }

        return name.Length == 0
            ? new DivisionRow(null, IAircraftPerformanceMetric.NotRecordedName, false, row.Figures, tails)
            : new DivisionRow(keys.GetValueOrDefault(name), name, false, row.Figures, tails);
    }

    private sealed record Row(int Kind, string? Division, string? Tail, PeriodFigures Figures);
}
