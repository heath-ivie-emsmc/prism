using Prism.Application.Metrics;

namespace Prism.Application.Attention;

/// <summary>How many claims one Attention Check flags. An aggregate: it carries no claim detail.</summary>
public sealed record AttentionCount(string Key, int Count);

/// <summary>The counts for every check in a period, and the balance still owed on the denied claims. Aggregates only.</summary>
public sealed record AttentionCounts(DateOnly? DataThrough, IReadOnlyList<AttentionCount> Counts, decimal DeniedBalanceDue);

/// <summary>
/// One claim an Attention Check flags. Every field can identify a patient's claim, so rows are PHI: they need the claims permission and an audit record.
/// <paramref name="BalanceDue"/> is what is still owed, not an estimate of what will be paid.
/// </summary>
public sealed record AttentionClaim(
    long EncounterId,
    string ClaimRef,
    DateOnly ServiceDate,
    string? Payer,
    string? PayerCategory,
    string? Vehicle,
    string ReportingDivision,
    string Status,
    string? DenialCode,
    string? DenialReason,
    decimal BalanceDue,
    DateOnly FilingDueDate,
    int DaysLeft);

/// <summary>The claims one check flags, soonest filing deadline first, the date the data runs to, and how many claims match in all (more than the rows when paged).</summary>
public sealed record AttentionClaims(DateOnly? DataThrough, IReadOnlyList<AttentionClaim> Claims, int Total);

/// <summary>One page of claims, counted from 1.</summary>
public sealed record AttentionPage(int Number, int Size);

/// <summary>
/// Reads the claims that need a customer's attention from the warehouse. Reads only. The Provider filter is applied here, from the id the access check produced.
/// Throws <see cref="Warehouse.WarehouseResultTooLargeException"/> when more claims match than the warehouse row cap allows.
/// </summary>
public interface IClaimAttention
{
    Task<AttentionCounts> GetCountsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken);

    // A null page returns every matching claim (the export). Either way, more than AttentionCatalog.MaxClaims matches is refused.
    Task<AttentionClaims> GetClaimsAsync(int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, AttentionPage? page, CancellationToken cancellationToken);
}
