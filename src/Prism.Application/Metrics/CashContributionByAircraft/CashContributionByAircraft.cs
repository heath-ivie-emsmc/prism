using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>
/// How much each tail number added to or took from the change in net cash against the month before. Net cash is posted cash (payments less refunds by posting month, without
/// the Old AR import) for encounters that have a trip row. Air only.
/// </summary>
public interface ICashContributionByAircraftMetric
{
    public static MetricDefinition Definition { get; } = new(
        "cash_contribution_by_aircraft",
        "Cash change by aircraft",
        "How much each tail number added to or took from the change in net cash against the month before (payments less refunds by posting month, without the Old AR import). The 25 tail numbers that moved cash most are listed, the rest are summed as Other, and the rows add up to the total change. Air only.",
        "USD");

    Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
