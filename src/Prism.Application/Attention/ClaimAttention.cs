using Prism.Application.Metrics;

namespace Prism.Application.Attention;

/// <summary>How many claims one Attention Check flags. An aggregate: it carries no claim detail.</summary>
public sealed record AttentionCount(string Key, int Count);

/// <summary>One category of claim needing action. A claim can be in more than one category, so the counts overlap and are not added together.</summary>
public sealed record AttentionCategoryCount(string Key, string Name, int Count, decimal BalanceDue);

/// <summary>One denial reason (cleaned, at most <see cref="AttentionCounts.ReasonMaxLength"/> characters) with the denied claims and balance due behind it. <paramref name="IsOther"/> marks the row that sums the rest.</summary>
public sealed record DenialReasonCount(string Reason, bool IsOther, int Count, decimal BalanceDue);

/// <summary>Needs-attention claims whose derived filing deadline falls in one band, counted from Data Through, and the balance due on them.</summary>
public sealed record FilingBandCount(string Key, string Name, int Claims, decimal BalanceDue);

/// <summary>The filing bands, in one place. Days left are counted from Data Through, so a deadline that is today has 0 days left and is not past due.</summary>
public static class FilingBands
{
    public const int SoonDays = 10;
    public const int NearDays = 30;

    public const string PastDue = "past_due";
    public const string DueWithin10Days = "due_within_10_days";
    public const string DueWithin30Days = "due_within_30_days";
    public const string DueLater = "due_later";

    public static IReadOnlyList<(string Key, string Name)> All { get; } =
    [
        (PastDue, "Past due"),
        (DueWithin10Days, "Due within 10 days"),
        (DueWithin30Days, "Due in 11 to 30 days"),
        (DueLater, "Due later"),
    ];

    /// <summary>The band a number of days left falls in. The SQL applies the same boundaries.</summary>
    public static string BandOf(int daysLeft) => daysLeft switch
    {
        < 0 => PastDue,
        <= SoonDays => DueWithin10Days,
        <= NearDays => DueWithin30Days,
        _ => DueLater,
    };
}

/// <summary>
/// The counts for every check in a period, and the balance still owed on the denied claims, plus the denial rate, the top denial reasons, the categories and the filing bands.
/// Aggregates only: no claim, patient or encounter value.
/// </summary>
public sealed record AttentionCounts(
    DateOnly? DataThrough,
    IReadOnlyList<AttentionCount> Counts,
    decimal DeniedBalanceDue,
    IReadOnlyList<AttentionCategoryCount>? Categories = null,
    IReadOnlyList<DenialReasonCount>? Reasons = null,
    IReadOnlyList<FilingBandCount>? Bands = null)
{
    /// <summary>Denial reasons listed one by one; the rest are summed in one Other row.</summary>
    public const int MaxReasons = 10;

    /// <summary>A reason is cut to this many characters in SQL.</summary>
    public const int ReasonMaxLength = 120;

    public const string NoReasonGiven = "No reason given";

    /// <summary>Denied claims divided by all claims in the period (0 to 1, four decimals). Null when there are no claims.</summary>
    public decimal? DeniedRate
    {
        get
        {
            int denied = Counts.FirstOrDefault(c => c.Key == AttentionCatalog.Denied)?.Count ?? 0;
            int all = Counts.FirstOrDefault(c => c.Key == AttentionCatalog.All)?.Count ?? 0;
            return all == 0 ? null : Math.Round((decimal)denied / all, 4, MidpointRounding.AwayFromZero);
        }
    }
}

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
