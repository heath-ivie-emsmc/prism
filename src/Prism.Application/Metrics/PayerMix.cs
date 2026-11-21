namespace Prism.Application.Metrics;

/// <summary>One payer group and how many transports had it as their primary payer in the chosen month and the one before.</summary>
public sealed record PayerMixShare(string Key, string Name, int Current, int Prior);

/// <summary>
/// Transports by primary payer group for one date-of-service month and the month before. <paramref name="GroupKey"/> narrows it to one aircraft type
/// or level of service, and is null for the whole Service Line. <paramref name="Shares"/> always lists every payer group, in a fixed order, with zeros.
/// </summary>
public sealed record PayerMix(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    string? GroupName,
    IReadOnlyList<PayerMixShare> Shares);

/// <summary>
/// Share of transports by primary payer. A transport belongs to the month it was flown or driven (its date of service), and its payer group is
/// the financial class of its primary payer: Commercial is Insurance and TPL, Medicare includes Medicare Advantage, Medicaid includes Medicaid MCO.
/// VA covers VA, CHAMPVA and TRICARE payers by name, whatever financial class the warehouse gave them.
/// </summary>
public interface IPayerMixMetric
{
    public static MetricDefinition Definition { get; } = new(
        "payer_mix",
        "Payer mix",
        "Transports by the primary payer's group (Commercial, Medicare, Medicaid, Other government, VA, Facility, Patient responsibility) in the month of service and the month before.",
        "transports");

    Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken);
}
