namespace Prism.Application.Attention;

/// <summary>The row rule an Attention Check applies. The infrastructure maps each to a fixed SQL fragment; request text never reaches SQL.</summary>
public enum AttentionPredicate
{
    /// <summary>A denial code is present and money is still owed.</summary>
    Denied,

    /// <summary>Denied, on hold, or in collections.</summary>
    NeedsAttention,

    /// <summary>Every claim in the period.</summary>
    All,
}

/// <summary>One named rule over claim data that flags items needing customer action.</summary>
public sealed record AttentionCheck(string Key, string Name, string Description, int Order, AttentionPredicate Predicate);

/// <summary>
/// The catalog of Attention Checks. Claim rows behind any of these need the claims permission and leave an audit record.
/// The tabs on the review page come from this list, so a new check is one entry here and one predicate in the infrastructure.
/// </summary>
public static class AttentionCatalog
{
    /// <summary>The most claims one check, period and filter may match. Past this the read is refused, not truncated.</summary>
    public const int MaxClaims = 10_000;

    public const string Denied = "denied";
    public const string NeedsAttention = "needs_attention";
    public const string All = "all";

    public static IReadOnlyList<AttentionCheck> Checks { get; } =
    [
        new(Denied, "Denied", "Claims with a denial code and money still owed.", 1, AttentionPredicate.Denied),
        new(NeedsAttention, "Needs attention", "Denied claims, claims on hold, and claims in collections.", 2, AttentionPredicate.NeedsAttention),
        new(All, "All", "Every claim for the period.", 3, AttentionPredicate.All),
    ];

    public static AttentionCheck? Find(string? key) =>
        key is null ? null : Checks.FirstOrDefault(check => string.Equals(check.Key, key, StringComparison.Ordinal));
}
