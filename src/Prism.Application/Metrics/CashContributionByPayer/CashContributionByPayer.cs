using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>How much each payer group added to or took from the change in net cash against the month before, by the payer of the transport the cash was posted against.</summary>
public interface ICashContributionByPayerMetric
{
    public static MetricDefinition Definition { get; } = new(
        "cash_contribution_by_payer",
        "Cash change by payer group",
        "How much each payer group (Commercial, Medicare, Medicaid, Other government, VA, Facility, Patient responsibility) added to or took from the change in net cash against the month before (payments less refunds by posting month, without the Old AR import). The rows add up to the total change.",
        "USD");

    Task<CashContribution> GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
