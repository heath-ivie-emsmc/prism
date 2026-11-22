namespace Prism.Application.Metrics;

/// <summary>
/// Net cash posted in one month for one aircraft type or level of service. <paramref name="Transports"/> are the encounters that had net cash posted
/// in the month, however long ago they were flown or driven, as in the breakdown.
/// </summary>
public sealed record NetCashByGroupPoint(string GroupKey, string GroupName, bool IsAir, ApPeriod Period, decimal Cash, int Transports);

/// <summary>
/// Net cash by posting month for each aircraft type or level of service, for the chosen period and the twelve months before it. Months with no cash
/// are left out, not returned as zero.
/// </summary>
public sealed record NetCashByGroup(MetricDefinition Metric, ApPeriod Period, int MonthsBefore, ServiceLineFilter ServiceLine, IReadOnlyList<NetCashByGroupPoint> Points);

/// <summary>
/// The monthly history behind a group's change in net cash. Net cash means what the breakdown means: credit transactions that are payments or refunds,
/// by the accounting period they posted in, without the Old AR import. Cash for an encounter with no trip row is left out because it has no group.
/// </summary>
public interface INetCashByGroupMetric
{
    /// <summary>The months before the chosen period that are included, so a year of month-to-month changes exists.</summary>
    public const int MonthsBefore = 12;

    public static MetricDefinition Definition { get; } = new(
        "net_cash_by_group",
        "Net cash by month and aircraft type or level of service",
        "Net cash collected by posting month (payments less refunds, without the Old AR import), split by aircraft type or level of service, for the chosen period and the twelve months before it.",
        "USD");

    Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken);
}
