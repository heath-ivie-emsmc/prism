using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports by pickup source from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter) by date-of-service month. The source is a bucket of
/// <c>origin_mod</c> (<see cref="PickupSql"/>). Facility-based sources are also split by <c>origin_facility</c>: the database keeps the top 20 across the whole result and sums
/// the rest into one Other row, and the facility text is cleaned in SQL. Scene, residence, custodial facility and other pickups are never broken down by name.
/// <b>PHI:</b> no query selects <c>origin_address</c>, <c>origin_city</c>, <c>origin_state</c> or <c>origin_zip</c> (a test asserts it on the SQL text), because on a scene or
/// home pickup they can be a patient's location. Net cash by source is by posting month from the ledger (<see cref="NetCashSql"/>). Only counts and sums leave the queries.
/// </summary>
public sealed class TransportsByPickupLocationMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ITransportsByPickupLocationMetric
{
    internal const string SourcesSql =
        $$"""
        WITH trips AS (
            SELECT origin_mod, prim_pay, prim_pay_category, dos, loaded_miles
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        )
        SELECT to_char(dos, 'YYYYMM') AS ym,
               {{PickupSql.SourceCase}} AS source,
               {{PayerGroupSql.CaseExpression}} AS payer,
               COUNT(*) AS transports,
               COALESCE(SUM(loaded_miles), 0) AS loaded_miles
        FROM trips
        GROUP BY 1, 2, 3
        """;

    // Row kinds: 1 a listed facility, 2 the sum of the other facilities.
    internal const string FacilitiesSql =
        $$"""
        WITH trips AS (
            SELECT origin_mod, origin_facility, dos, loaded_miles
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        ),
        tagged AS (
            SELECT to_char(dos, 'YYYYMM') AS ym, {{PickupSql.SourceCase}} AS source, loaded_miles,
                   {{TripSql.CleanFacility}} AS facility
            FROM trips
        ),
        facility_rows AS (
            SELECT source, facility,
                   COUNT(*) FILTER (WHERE ym = @period) AS cur_t,
                   COUNT(*) FILTER (WHERE ym = @prior) AS prior_t,
                   COALESCE(SUM(loaded_miles) FILTER (WHERE ym = @period), 0) AS cur_m
            FROM tagged
            WHERE source IN {{PickupSql.FacilityBasedKeys}}
            GROUP BY source, facility
        ),
        ranked AS (
            SELECT *, ROW_NUMBER() OVER (ORDER BY cur_t DESC, prior_t DESC, source, facility) AS rn
            FROM facility_rows
        )
        SELECT 1 AS kind, source, facility, cur_t::bigint, prior_t::bigint, cur_m FROM ranked WHERE rn <= @top
        UNION ALL
        SELECT 2, NULL, NULL, SUM(cur_t)::bigint, SUM(prior_t)::bigint, SUM(cur_m) FROM ranked WHERE rn > @top HAVING COUNT(*) > 0
        """;

    // Cash by the month it posted, for the encounters of the Provider's latest load. An encounter with no trip row has no source and is left out.
    internal const string CashSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, {{PickupSql.SourceCase}} AS source
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        )
        SELECT t.source, cash.ym, SUM(cash.amount) AS amount
        FROM cash
        JOIN trips t ON t.encounter_id = cash.encounter_id
        GROUP BY 1, 2
        """;

    public async Task<TransportsByPickupLocation> GetPickupsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod next = period.AddMonths(1);
        int warehouseId = await providers.ResolveAsync(providerId, cancellationToken);
        var scope = new Dictionary<string, object?>
        {
            [WarehouseQuery.ProviderParameter] = warehouseId,
            ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
            ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
            [DivisionSql.Parameter] = division?.Name ?? string.Empty,
            ["group_key"] = groupKey ?? string.Empty,
        };
        var window = new Dictionary<string, object?>(scope)
        {
            ["from_date"] = new DateOnly(prior.Year, prior.Month, 1),
            ["to_date"] = new DateOnly(next.Year, next.Month, 1),
        };

        IReadOnlyList<SourceRow> sources = await warehouse.QueryAsync(
            new WarehouseQuery(SourcesSql, window) { Name = "TransportsByPickupLocationMetric" },
            reader => new SourceRow(reader.GetString(0).Trim(), reader.GetString(1), reader.GetString(2), (int)reader.GetInt64(3), reader.GetDecimal(4)),
            cancellationToken);

        var facilityParameters = new Dictionary<string, object?>(window)
        {
            ["period"] = period.ToString(),
            ["prior"] = prior.ToString(),
            ["top"] = ITransportsByPickupLocationMetric.TopFacilities,
            ["max_name"] = ITransportsByPickupLocationMetric.MaxNameLength,
        };
        IReadOnlyList<FacilityRow> facilities = await warehouse.QueryAsync(
            new WarehouseQuery(FacilitiesSql, facilityParameters) { Name = "TransportsByPickupLocationMetric#facilities" },
            reader => new FacilityRow(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), (int)reader.GetInt64(3), (int)reader.GetInt64(4), reader.GetDecimal(5)),
            cancellationToken);

        var cashParameters = new Dictionary<string, object?>(scope)
        {
            ["period"] = period.ToString(),
            ["prior"] = prior.ToString(),
        };
        IReadOnlyList<CashRow> cash = await warehouse.QueryAsync(
            new WarehouseQuery(CashSql, cashParameters) { Name = "TransportsByPickupLocationMetric#cash" },
            reader => new CashRow(reader.GetString(0), reader.GetString(1).Trim(), reader.GetDecimal(2)),
            cancellationToken);

        return Build(period, prior, serviceLine, groupKey, sources, facilities, cash);
    }

    private static TransportsByPickupLocation Build(ApPeriod period, ApPeriod prior, ServiceLineFilter serviceLine, string? groupKey, IReadOnlyList<SourceRow> sources, IReadOnlyList<FacilityRow> facilities, IReadOnlyList<CashRow> cash)
    {
        string current = period.ToString();
        string previous = prior.ToString();
        HashSet<string> present = [.. sources.Select(r => r.Source), .. cash.Select(r => r.Source)];

        List<PickupSource> rows = [.. ITransportsByPickupLocationMetric.Sources.Where(s => present.Contains(s.Key)).Select(s =>
        {
            IEnumerable<SourceRow> own = sources.Where(r => r.Source == s.Key);
            List<PayerCount> payers = [.. PayerMixMetric.Groups
                .Select(g => new PayerCount(
                    g.Key,
                    g.Name,
                    own.Where(r => r.Payer == g.Key && r.Period == current).Sum(r => r.Transports),
                    own.Where(r => r.Payer == g.Key && r.Period == previous).Sum(r => r.Transports)))
                .Where(p => p.Transports + p.PriorTransports > 0)];
            return new PickupSource(
                s.Key,
                s.Name,
                s.FacilityBased,
                own.Where(r => r.Period == current).Sum(r => r.Transports),
                own.Where(r => r.Period == previous).Sum(r => r.Transports),
                own.Where(r => r.Period == current).Sum(r => r.LoadedMiles),
                own.Where(r => r.Period == previous).Sum(r => r.LoadedMiles),
                cash.Where(c => c.Source == s.Key && c.Period == current).Sum(c => c.Amount),
                cash.Where(c => c.Source == s.Key && c.Period == previous).Sum(c => c.Amount),
                payers);
        })];

        // Only a facility-based source ever carries a facility name, whatever the query returned.
        HashSet<string> facilityBased = [.. ITransportsByPickupLocationMetric.Sources.Where(s => s.FacilityBased).Select(s => s.Key)];
        List<PickupFacility> named = [.. facilities
            .Where(f => f.Kind == 1 && f.Source is not null && facilityBased.Contains(f.Source))
            .Select(f => new PickupFacility(f.Source, DisplayFacility(f.Facility), false, f.Current, f.Prior, f.Miles))];
        if (facilities.FirstOrDefault(f => f.Kind == 2) is { } rest)
        {
            named.Add(new PickupFacility(null, ITransportsByPickupLocationMetric.OtherFacilityName, true, rest.Current, rest.Prior, rest.Miles));
        }

        return new TransportsByPickupLocation(ITransportsByPickupLocationMetric.Definition, period, prior, serviceLine, groupKey, rows, named);
    }

    // A real facility called "Other" must not look like the roll-up row.
    internal static string DisplayFacility(string? name) =>
        string.Equals(name, ITransportsByPickupLocationMetric.OtherFacilityName, StringComparison.OrdinalIgnoreCase) ? "Other facility" : name ?? "Not recorded";

    private sealed record SourceRow(string Period, string Source, string Payer, int Transports, decimal LoadedMiles);

    private sealed record FacilityRow(int Kind, string? Source, string? Facility, int Current, int Prior, decimal Miles);

    private sealed record CashRow(string Source, string Period, decimal Amount);
}
