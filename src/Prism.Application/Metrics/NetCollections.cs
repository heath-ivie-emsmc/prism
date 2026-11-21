namespace Prism.Application.Metrics;

/// <summary>A Metric as the API describes it. Defined once here, so cards, charts and the public API say the same thing.</summary>
public sealed record MetricDefinition(string Key, string Name, string Description, string Unit);

/// <summary>Net cash collected in one accounting period.</summary>
public sealed record NetCollectionsPoint(ApPeriod Period, decimal Value);

/// <summary>
/// A run of months of net cash for a Provider.
/// <paramref name="SnapshotDate"/> is the date of the warehouse load the figures come from, not a calendar month.
/// </summary>
public sealed record NetCollectionsSeries(
    MetricDefinition Metric,
    ApPeriod EndPeriod,
    int Months,
    DateOnly? SnapshotDate,
    IReadOnlyList<NetCollectionsPoint> Points);

/// <summary>The first Metric: net cash collected by accounting period. The Provider is always the one the caller was already allowed to see.</summary>
public interface INetCollectionsMetric
{
    public const int MaxMonths = 24;
    public const int DefaultMonths = 6;

    public static MetricDefinition Definition { get; } = new(
        "net_collections",
        "Net cash collected",
        "Cash collected in the accounting period, after refunds. Counted by the period the payment was posted in, from the latest warehouse load.",
        "USD");

    /// <summary>The accounting periods that have data, newest first. Fills the Period drop-down.</summary>
    Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken);

    /// <summary>The <paramref name="months"/> months that end with <paramref name="endPeriod"/>. Months with no data are left out, never shown as zero.</summary>
    Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken);
}
