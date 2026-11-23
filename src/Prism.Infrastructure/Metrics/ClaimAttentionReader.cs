using System.Data.Common;
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

    public async Task<AttentionClaims> GetClaimsAsync(
        int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, AttentionPage? page, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = await ParametersAsync(providerId, period, serviceLine, groupKey, cancellationToken);
        FilingLimitOptions filing = limits.Value;
        parameters["limit_categories"] = filing.ByPayerCategory.Keys.ToArray();
        parameters["limit_days"] = filing.ByPayerCategory.Values.ToArray();
        parameters["default_limit_days"] = filing.Default;
        // No page means every claim: a null limit is "no limit" in PostgreSQL.
        parameters["page_size"] = page is null ? null : page.Size;
        parameters["page_offset"] = page is null ? 0 : (page.Number - 1) * page.Size;

        var query = new WarehouseQuery(ClaimAttentionSql.ClaimsSql(check.Predicate), parameters) { Name = "ClaimAttentionRows" };
        IReadOnlyList<(AttentionClaim Claim, DateOnly Through, long Total)> rows = await warehouse.QueryAsync(query, reader => ReadClaim(reader, filing), cancellationToken);

        long total = rows.Count > 0 ? rows[0].Total : await TotalBeyondLastPageAsync(page, providerId, check, period, serviceLine, groupKey, cancellationToken);
        if (total > AttentionCatalog.MaxClaims)
        {
            throw new WarehouseResultTooLargeException(AttentionCatalog.MaxClaims);
        }

        return new AttentionClaims(rows.Count == 0 ? null : rows[0].Through, rows.Select(r => r.Claim).ToArray(), (int)total);
    }

    // A page past the end has no rows to carry the window count, so ask for the first row only to learn the total.
    private async Task<long> TotalBeyondLastPageAsync(
        AttentionPage? page, int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        if (page is null or { Number: 1 })
        {
            return 0;
        }

        AttentionClaims first = await GetClaimsAsync(providerId, check, period, serviceLine, groupKey, new AttentionPage(1, 1), cancellationToken);
        return first.Total;
    }

    private static (AttentionClaim Claim, DateOnly Through, long Total) ReadClaim(DbDataReader reader, FilingLimitOptions filing)
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
        return (claim, dataThrough, reader.GetInt64(12));
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
