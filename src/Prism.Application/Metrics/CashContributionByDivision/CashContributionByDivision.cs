using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>How much each Reporting Division added to or took from the change in net cash against the month before.</summary>
public interface ICashContributionByDivisionMetric
{
    public static MetricDefinition Definition { get; } = new(
        "cash_contribution_by_division",
        "Cash change by Reporting Division",
        "How much each Reporting Division added to or took from the change in net cash against the month before (payments less refunds by posting month, without the Old AR import). The 25 divisions that moved cash most are listed, the rest are summed as Other, and the rows add up to the total change.",
        "USD");

    Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
