using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>
/// Net cash posted in one month for one payer group, from transports of one age. <paramref name="AgeMonths"/> is the posting month minus the
/// date-of-service month, so 0 is cash for a transport flown or driven that same month. Ages above <see cref="IPaymentAgeMetric.MaxAgeMonths"/>
/// are counted at that age.
/// </summary>
public sealed record PaymentAgeCell(string PayerKey, string PayerName, ApPeriod PostingPeriod, int AgeMonths, decimal Cash, int Transports);

/// <summary>
/// Net cash by how old the transport was when it was paid, by payer group and posting month, for the chosen period and the months before it.
/// <paramref name="GroupKey"/> narrows it to one aircraft type or level of service, and is null for the whole Service Line.
/// </summary>
public sealed record PaymentAge(
    MetricDefinition Metric,
    ApPeriod Period,
    int MonthsBefore,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<PaymentAgeCell> Cells);

/// <summary>
/// How long payers take to pay. Cash is the net cash the ledger posted (payments less refunds, without the Old AR import), and its age comes from the
/// date of service of the transport it was paid for, so cash for transports with no date of service is left out. Aggregates only.
/// </summary>
public interface IPaymentAgeMetric
{
    /// <summary>Older payments are counted at this age.</summary>
    public const int MaxAgeMonths = 24;

    /// <summary>The months before the chosen period that are included, so a baseline exists.</summary>
    public const int MonthsBefore = 12;

    public static MetricDefinition Definition { get; } = new(
        "payment_age",
        "Net cash by payment age and payer",
        "Net cash by payer group and posting month, split by how many months passed between the date of service and the posting month, for the chosen period and the twelve months before it.",
        "USD");

    Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
