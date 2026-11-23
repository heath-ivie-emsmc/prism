namespace Prism.Application.Metrics;

/// <summary>Claims for one month of service: how many were accepted first time, how many were rejected, and the total.</summary>
public sealed record CleanClaimPoint(ApPeriod Period, int CleanClaims, int RejectedClaims, int TotalClaims)
{
    /// <summary>Clean claims as a share of all claims, 0 to 1, or null when there were no claims.</summary>
    public decimal? Rate => TotalClaims == 0 ? null : decimal.Round((decimal)CleanClaims / TotalClaims, 4);
}

/// <summary>
/// The clean claim rate by month of service, newest <paramref name="Months"/> months up to <paramref name="EndPeriod"/> that the warehouse has.
/// <paramref name="SnapshotDate"/> is the load the figures came from, or null when there are none. The warehouse figure is not split by
/// Service Line, so it covers all of the Provider's claims.
/// </summary>
public sealed record CleanClaimRate(
    MetricDefinition Metric,
    ApPeriod EndPeriod,
    int Months,
    DateOnly? SnapshotDate,
    IReadOnlyList<CleanClaimPoint> Points);

/// <summary>
/// The share of claims accepted first time, by month of service, from the warehouse's clean claim rate table. A month is only in the series if
/// the warehouse has a figure for it. The table is refreshed on its own schedule, so the newest month can be well behind the other Metrics.
/// </summary>
public interface ICleanClaimRateMetric
{
    public const int MaxMonths = 24;

    public static MetricDefinition Definition { get; } = new(
        "clean_claim_rate",
        "Clean claim rate",
        "The share of claims accepted first time, with no rejection, by month of service. Covers all of the Provider's claims: the warehouse does not split it by Service Line.",
        "percent");

    Task<CleanClaimRate> GetRateAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken);
}
