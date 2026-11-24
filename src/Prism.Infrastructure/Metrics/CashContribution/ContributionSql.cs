using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>What a contribution query is asked for. The Provider is the portal id the route was authorized for.</summary>
internal sealed record ContributionRequest(int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? GroupKey, ReportingDivision? Division);

/// <summary>One listed contributor (<see cref="Other"/> false) or the sum of every contributor past the cap (<see cref="Other"/> true), with net cash in the month and the month before.</summary>
internal sealed record ContributionLine(bool Other, string? Item, decimal NetCash, decimal PriorNetCash);

/// <summary>
/// The SQL the three contribution Metrics share. Each Metric writes only its own <c>trips</c> CTE (encounter id and the contributor, <c>item</c>) and the rest is one copy:
/// net cash by posting month for the month and the one before (<see cref="NetCashSql"/>), added up per contributor, the 25 that moved cash most by absolute change listed, and
/// one row that sums the rest. Because every contributor and the roll-up come from the same rows, they add up to the total. The Provider filter is in <see cref="TripSql.LatestLoad"/>
/// and in the accounting periods. Only sums leave the query.
/// </summary>
internal static class ContributionSql
{
    /// <summary>The Provider's newest-load trip rows in the Service Line, group and Reporting Division asked for.</summary>
    internal const string Scope = $"{TripSql.LatestLoad} AND {DivisionSql.Filter} AND {TripSql.ServiceLine} AND {TripSql.Group}";

    /// <summary>Everything after the <c>trips</c> CTE. Row kinds: 1 is a listed contributor, 2 the sum of the others.</summary>
    internal const string Ranking =
        """
        paid AS (
            SELECT t.item,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @period), 0) AS cur_c,
                   COALESCE(SUM(cash.amount) FILTER (WHERE cash.ym = @prior), 0) AS prior_c
            FROM cash
            JOIN trips t ON t.encounter_id = cash.encounter_id
            GROUP BY t.item
        ),
        ranked AS (
            SELECT *, ROW_NUMBER() OVER (ORDER BY abs(cur_c - prior_c) DESC, item) AS rn
            FROM paid
        )
        SELECT 1 AS kind, item, cur_c, prior_c FROM ranked WHERE rn <= @top
        UNION ALL
        SELECT 2, NULL, SUM(cur_c), SUM(prior_c) FROM ranked WHERE rn > @top HAVING COUNT(*) > 0
        """;

    internal static async Task<IReadOnlyList<ContributionLine>> ReadAsync(
        IWarehouse warehouse,
        IWarehouseProviderResolver providers,
        string sql,
        string queryName,
        ContributionRequest request,
        CancellationToken cancellationToken)
    {
        ApPeriod prior = request.Period.AddMonths(-1);
        var parameters = new Dictionary<string, object?>
        {
            [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(request.ProviderId, cancellationToken),
            ["period"] = request.Period.ToString(),
            ["prior"] = prior.ToString(),
            ["include_air"] = request.ServiceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
            ["include_ground"] = request.ServiceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
            [DivisionSql.Parameter] = request.Division?.Name ?? string.Empty,
            ["group_key"] = request.GroupKey ?? string.Empty,
            ["top"] = Contribution.TopRows,
        };

        // The warehouse refuses a parameter the query does not use, and only the tail text is cut to a length.
        if (sql.Contains("@max_name", StringComparison.Ordinal))
        {
            parameters["max_name"] = IAircraftPerformanceMetric.MaxNameLength;
        }

        var query = new WarehouseQuery(sql, parameters) { Name = queryName };

        return await warehouse.QueryAsync(
            query,
            reader => new ContributionLine(reader.GetInt32(0) == 2, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)),
            cancellationToken);
    }

    /// <summary>Largest movers first (ties by name), then the Other row. Sorting here keeps the order independent of the database's collation.</summary>
    internal static List<ContributionRow> Order(IEnumerable<ContributionRow> listed, IEnumerable<ContributionRow> other) =>
    [
        .. listed.OrderByDescending(r => Math.Abs(r.Change)).ThenBy(r => r.Name, StringComparer.Ordinal),
        .. other,
    ];
}
