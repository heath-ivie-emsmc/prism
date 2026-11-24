using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// Transports by call type from <c>reporting.trip_detail_snapshot</c> (latest load, one row per encounter) by date-of-service month, with loaded miles and the primary payer
/// group of each transport, plus net cash by call type from the transaction ledger by posting month (<see cref="NetCashSql"/>). The Provider filter is in the Catalog.
/// Only counts and sums leave the queries: no patient, claim or payer names.
/// </summary>
public sealed class TransportMixByCallTypeMetric(IWarehouse warehouse, IWarehouseProviderResolver providers) : ITransportMixByCallTypeMetric
{
    internal const string MixSql =
        $$"""
        WITH trips AS (
            SELECT level_of_service, response_type, prim_pay, prim_pay_category, dos, loaded_miles
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND dos >= @from_date AND dos < @to_date
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        )
        SELECT to_char(dos, 'YYYYMM') AS ym,
               {{CallTypeSql.CaseExpression}} AS call_type,
               {{PayerGroupSql.CaseExpression}} AS payer,
               COUNT(*) AS transports,
               COALESCE(SUM(loaded_miles), 0) AS loaded_miles
        FROM trips
        GROUP BY 1, 2, 3
        """;

    // Cash by the month it posted, for the encounters of the Provider's latest load. An encounter with no trip row has no call type and is left out.
    internal const string CashSql =
        $$"""
        WITH {{TripSql.TwoPeriods}},
        {{NetCashSql.CashCte}},
        trips AS (
            SELECT encounter_id, {{CallTypeSql.CaseExpression}} AS call_type
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND {{DivisionSql.Filter}}
              AND {{TripSql.ServiceLine}}
              AND {{TripSql.Group}}
        )
        SELECT t.call_type, cash.ym, SUM(cash.amount) AS amount
        FROM cash
        JOIN trips t ON t.encounter_id = cash.encounter_id
        GROUP BY 1, 2
        """;

    public async Task<TransportMixByCallType> GetMixAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        int count = TrendMonths.IsAllowed(months) ? months : TrendMonths.Default;
        ApPeriod prior = period.AddMonths(-1);
        ApPeriod first = period.AddMonths(-(count - 1));
        ApPeriod next = period.AddMonths(1);
        int warehouseId = await providers.ResolveAsync(providerId, cancellationToken);
        string group = groupKey ?? string.Empty;
        string divisionName = division?.Name ?? string.Empty;
        bool air = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air;
        bool ground = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground;

        var mixQuery = new WarehouseQuery(
            MixSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = warehouseId,
                ["from_date"] = new DateOnly(first.Year, first.Month, 1),
                ["to_date"] = new DateOnly(next.Year, next.Month, 1),
                ["include_air"] = air,
                ["include_ground"] = ground,
                [DivisionSql.Parameter] = divisionName,
                ["group_key"] = group,
            }) { Name = "TransportMixByCallTypeMetric" };
        var cashQuery = new WarehouseQuery(
            CashSql,
            new Dictionary<string, object?>
            {
                [WarehouseQuery.ProviderParameter] = warehouseId,
                ["period"] = period.ToString(),
                ["prior"] = prior.ToString(),
                ["include_air"] = air,
                ["include_ground"] = ground,
                [DivisionSql.Parameter] = divisionName,
                ["group_key"] = group,
            }) { Name = "TransportMixByCallTypeMetric#cash" };

        IReadOnlyList<MixRow> mix = await warehouse.QueryAsync(
            mixQuery,
            reader => new MixRow(reader.GetString(0).Trim(), reader.GetString(1), reader.GetString(2), (int)reader.GetInt64(3), reader.GetDecimal(4)),
            cancellationToken);
        IReadOnlyList<CashRow> cash = await warehouse.QueryAsync(
            cashQuery,
            reader => new CashRow(reader.GetString(0), reader.GetString(1).Trim(), reader.GetDecimal(2)),
            cancellationToken);

        return Build(new MixInput(period, prior, count, serviceLine, groupKey), mix, cash);
    }

    private static TransportMixByCallType Build(MixInput input, IReadOnlyList<MixRow> mix, IReadOnlyList<CashRow> cash)
    {
        string current = input.Period.ToString();
        string previous = input.Prior.ToString();
        HashSet<string> present = [.. mix.Select(r => r.CallType), .. cash.Select(r => r.CallType)];
        (string Key, string Name)[] types = [.. ITransportMixByCallTypeMetric.CallTypes.Where(t => present.Contains(t.Key))];

        List<CallTypeRow> rows = [.. types.Select(t =>
        {
            IEnumerable<MixRow> own = mix.Where(r => r.CallType == t.Key);
            List<PayerCount> payers = [.. PayerMixMetric.Groups
                .Select(g => new PayerCount(
                    g.Key,
                    g.Name,
                    own.Where(r => r.Payer == g.Key && r.Period == current).Sum(r => r.Transports),
                    own.Where(r => r.Payer == g.Key && r.Period == previous).Sum(r => r.Transports)))
                .Where(p => p.Transports + p.PriorTransports > 0)];
            return new CallTypeRow(
                t.Key,
                t.Name,
                own.Where(r => r.Period == current).Sum(r => r.Transports),
                own.Where(r => r.Period == previous).Sum(r => r.Transports),
                own.Where(r => r.Period == current).Sum(r => r.LoadedMiles),
                own.Where(r => r.Period == previous).Sum(r => r.LoadedMiles),
                cash.Where(c => c.CallType == t.Key && c.Period == current).Sum(c => c.Amount),
                cash.Where(c => c.CallType == t.Key && c.Period == previous).Sum(c => c.Amount),
                payers);
        })];

        List<CallTypeTrendPoint> trend = [.. Enumerable.Range(0, input.Months)
            .Select(i => input.Period.AddMonths(-(input.Months - 1 - i)))
            .Select(p => new CallTypeTrendPoint(p, [.. types.Select(t => new CallTypeCount(t.Key, mix.Where(r => r.CallType == t.Key && r.Period == p.ToString()).Sum(r => r.Transports)))]))];

        return new TransportMixByCallType(ITransportMixByCallTypeMetric.Definition, input.Period, input.Prior, input.Months, input.ServiceLine, input.GroupKey, rows, trend);
    }

    private readonly record struct MixInput(ApPeriod Period, ApPeriod Prior, int Months, ServiceLineFilter ServiceLine, string? GroupKey);

    private sealed record MixRow(string Period, string CallType, string Payer, int Transports, decimal LoadedMiles);

    private sealed record CashRow(string CallType, string Period, decimal Amount);
}
