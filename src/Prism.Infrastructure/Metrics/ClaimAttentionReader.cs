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
        Dictionary<string, object?> parameters = await ParametersAsync(providerId, period, serviceLine, groupKey, cancellationToken);
        var query = new WarehouseQuery(ClaimAttentionSql.CountsSql, parameters) { Name = "ClaimAttentionCounts" };

        IReadOnlyList<AttentionCounts> rows = await warehouse.QueryAsync(
            query,
            reader => new AttentionCounts(
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                [
                    new AttentionCount(AttentionCatalog.Denied, (int)reader.GetInt64(0)),
                    new AttentionCount(AttentionCatalog.NeedsAttention, (int)reader.GetInt64(1)),
                    new AttentionCount(AttentionCatalog.All, (int)reader.GetInt64(2)),
                ],
                reader.GetDecimal(3),
                [
                    new AttentionCategoryCount(AttentionCatalog.Denied, "Denied", (int)reader.GetInt64(0), reader.GetDecimal(3)),
                    new AttentionCategoryCount("on_hold", "On hold", (int)reader.GetInt64(5), reader.GetDecimal(6)),
                    new AttentionCategoryCount("in_collections", "In collections", (int)reader.GetInt64(7), reader.GetDecimal(8)),
                ]),
            cancellationToken);

        // An aggregate always returns one row; a period with no claims has no load date, and then the follow-up reads are skipped.
        if (rows.Count != 1 || rows[0].DataThrough is null)
        {
            return new AttentionCounts(null, AttentionCatalog.Checks.Select(c => new AttentionCount(c.Key, 0)).ToList(), 0, EmptyCategories(), [], EmptyBands());
        }

        // The two follow-up reads only run when the period has claims, and use the same scope.
        IReadOnlyList<DenialReasonCount> reasons = await ReadReasonsAsync(parameters, cancellationToken);
        IReadOnlyList<FilingBandCount> bands = await ReadBandsAsync(parameters, cancellationToken);
        return rows[0] with { Reasons = reasons, Bands = bands };
    }

    private async Task<IReadOnlyList<DenialReasonCount>> ReadReasonsAsync(Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var query = new WarehouseQuery(ClaimAttentionSql.ReasonsSql, parameters) { Name = "ClaimAttentionReasons" };
        return await warehouse.QueryAsync(
            query,
            reader => new DenialReasonCount(reader.GetString(0), reader.GetBoolean(1), (int)reader.GetDecimal(2), reader.GetDecimal(3)),
            cancellationToken);
    }

    private async Task<IReadOnlyList<FilingBandCount>> ReadBandsAsync(Dictionary<string, object?> scope, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>(scope);
        AddFilingLimits(parameters);
        var query = new WarehouseQuery(ClaimAttentionSql.FilingBandsSql, parameters) { Name = "ClaimAttentionFilingBands" };
        IReadOnlyList<IReadOnlyList<FilingBandCount>> rows = await warehouse.QueryAsync(
            query,
            reader => FilingBands.All.Select((band, i) => new FilingBandCount(band.Key, band.Name, (int)reader.GetInt64(i * 2), reader.GetDecimal((i * 2) + 1))).ToList(),
            cancellationToken);
        return rows.Count == 1 ? rows[0] : EmptyBands();
    }

    private void AddFilingLimits(Dictionary<string, object?> parameters)
    {
        FilingLimitOptions filing = limits.Value;
        parameters["limit_categories"] = filing.ByPayerCategory.Keys.ToArray();
        parameters["limit_days"] = filing.ByPayerCategory.Values.ToArray();
        parameters["default_limit_days"] = filing.Default;
    }

    private static List<AttentionCategoryCount> EmptyCategories() =>
        [new(AttentionCatalog.Denied, "Denied", 0, 0m), new("on_hold", "On hold", 0, 0m), new("in_collections", "In collections", 0, 0m)];

    private static List<FilingBandCount> EmptyBands() => FilingBands.All.Select(b => new FilingBandCount(b.Key, b.Name, 0, 0m)).ToList();

    public async Task<AttentionClaims> GetClaimsAsync(
        int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, AttentionPage? page, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = await ParametersAsync(providerId, period, serviceLine, groupKey, cancellationToken);
        FilingLimitOptions filing = limits.Value;
        AddFilingLimits(parameters);
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
