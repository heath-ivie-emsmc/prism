using Microsoft.Extensions.Options;
using Prism.Application.Attention;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>Reads claim rows for the Attention Checks. The filing deadline is derived here (see <see cref="FilingDeadline"/>), so the warehouse needs no such column.</summary>
public sealed class ClaimAttentionReader(IWarehouse warehouse, IWarehouseProviderResolver providers, IOptions<FilingLimitOptions> limits) : IClaimAttention
{
    public async Task<AttentionCounts> GetCountsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        var query = new WarehouseQuery(ClaimAttentionSql.CountsSql, await ParametersAsync(providerId, period, serviceLine, groupKey, cancellationToken)) { Name = "ClaimAttentionCounts" };

        IReadOnlyList<AttentionCounts> rows = await warehouse.QueryAsync(
            query,
            reader => new AttentionCounts(
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                [
                    new AttentionCount(AttentionCatalog.Denied, (int)reader.GetInt64(0)),
                    new AttentionCount(AttentionCatalog.NeedsAttention, (int)reader.GetInt64(1)),
                    new AttentionCount(AttentionCatalog.All, (int)reader.GetInt64(2)),
                ],
                reader.GetDecimal(3)),
            cancellationToken);

        return rows.Count == 1 ? rows[0] : new AttentionCounts(null, AttentionCatalog.Checks.Select(c => new AttentionCount(c.Key, 0)).ToList(), 0);
    }

    public async Task<AttentionClaims> GetClaimsAsync(int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        var query = new WarehouseQuery(ClaimAttentionSql.ClaimsSql(check.Predicate), await ParametersAsync(providerId, period, serviceLine, groupKey, cancellationToken)) { Name = "ClaimAttentionRows" };
        FilingLimitOptions filing = limits.Value;

        IReadOnlyList<(AttentionClaim Claim, DateOnly Through)> rows = await warehouse.QueryAsync(
            query,
            reader =>
            {
                DateOnly serviceDate = DateOnly.FromDateTime(reader.GetDateTime(2));
                DateOnly dataThrough = DateOnly.FromDateTime(reader.GetDateTime(11));
                string? payerCategory = reader.IsDBNull(4) ? null : reader.GetString(4);
                DateOnly due = FilingDeadline.DueDate(serviceDate, filing.LimitFor(payerCategory));
                var claim = new AttentionClaim(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    serviceDate,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    payerCategory,
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetDecimal(10),
                    due,
                    FilingDeadline.DaysLeft(due, dataThrough));
                return (claim, dataThrough);
            },
            cancellationToken);

        // Soonest deadline first (overdue claims come first), then oldest service date, then the id so the order is stable across pages.
        AttentionClaim[] sorted = rows.Select(r => r.Claim).OrderBy(c => c.DaysLeft).ThenBy(c => c.ServiceDate).ThenBy(c => c.EncounterId).ToArray();
        return new AttentionClaims(rows.Count == 0 ? null : rows[0].Through, sorted);
    }

    private async Task<Dictionary<string, object?>> ParametersAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken) => new()
    {
        [WarehouseQuery.ProviderParameter] = await providers.ResolveAsync(providerId, cancellationToken),
        ["period_start"] = new DateOnly(period.Year, period.Month, 1),
        ["period_end"] = new DateOnly(period.Year, period.Month, 1).AddMonths(1),
        ["include_air"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Air,
        ["include_ground"] = serviceLine is ServiceLineFilter.Both or ServiceLineFilter.Ground,
        ["group_key"] = groupKey?.Trim().ToLowerInvariant() ?? "",
    };
}
