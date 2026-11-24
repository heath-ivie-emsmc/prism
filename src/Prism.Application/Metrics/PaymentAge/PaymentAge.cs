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
    IReadOnlyList<PaymentAgeCell> Cells)
{
    /// <summary>Per payer group: the typical age and the share of cash by age band, over all the cells (the chosen period and the months before it).</summary>
    public IReadOnlyList<PayerPaymentAge> Payers => PaymentAgeBands.Summarize(Cells);
}

/// <summary>One age band in whole months: <paramref name="FromMonths"/> to <paramref name="ToMonths"/> inclusive, open-ended (null) for the last.</summary>
public sealed record PaymentAgeBand(string Key, int FromMonths, int? ToMonths);

/// <summary>The share (0 to 1) of a payer group's cash that arrived in one age band.</summary>
public sealed record PaymentAgeBandShare(PaymentAgeBand Band, decimal Share);

/// <summary>
/// A payer group's typical payment age and age bands. <paramref name="TypicalAgeMonths"/> is the cash-weighted median age in months, null when the group has no cash.
/// <paramref name="Bands"/> is empty when the group has no cash.
/// </summary>
public sealed record PayerPaymentAge(string PayerKey, string PayerName, int? TypicalAgeMonths, IReadOnlyList<PaymentAgeBandShare> Bands);

/// <summary>
/// The payment age bands, in one place, and the arithmetic over the cells. Age is whole months (posting month minus date-of-service month, capped at
/// <see cref="IPaymentAgeMetric.MaxAgeMonths"/>): the ledger has no day-level payment date, so nothing finer is claimed. Only cells with positive cash weigh in,
/// so refunds and reversals do not push a share below zero or above one.
/// </summary>
public static class PaymentAgeBands
{
    public static IReadOnlyList<PaymentAgeBand> All { get; } =
    [
        new("0-1", 0, 1),
        new("2-3", 2, 3),
        new("4-6", 4, 6),
        new("7-12", 7, 12),
        new("13+", 13, null),
    ];

    public static IReadOnlyList<PayerPaymentAge> Summarize(IEnumerable<PaymentAgeCell> cells) =>
        cells
            .GroupBy(c => (c.PayerKey, c.PayerName))
            .OrderBy(g => g.Key.PayerKey, StringComparer.Ordinal)
            .Select(g => SummarizePayer(g.Key.PayerKey, g.Key.PayerName, g.Where(c => c.Cash > 0m).ToList()))
            .ToList();

    private static PayerPaymentAge SummarizePayer(string key, string name, List<PaymentAgeCell> positive)
    {
        decimal total = positive.Sum(c => c.Cash);
        if (total <= 0m)
        {
            return new PayerPaymentAge(key, name, null, []);
        }

        // Cash-weighted median: the first age at which the running cash reaches half of the total.
        decimal running = 0m;
        int typical = 0;
        foreach (IGrouping<int, PaymentAgeCell> age in positive.GroupBy(c => c.AgeMonths).OrderBy(a => a.Key))
        {
            running += age.Sum(c => c.Cash);
            typical = age.Key;
            if (running * 2m >= total)
            {
                break;
            }
        }

        List<PaymentAgeBandShare> bands = All
            .Select(b => new PaymentAgeBandShare(b, Math.Round(positive.Where(c => c.AgeMonths >= b.FromMonths && (b.ToMonths is not { } to || c.AgeMonths <= to)).Sum(c => c.Cash) / total, 4, MidpointRounding.AwayFromZero)))
            .ToList();
        return new PayerPaymentAge(key, name, typical, bands);
    }
}

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
        "Net cash by payer group and posting month, split by how many months passed between the date of service and the posting month, for the chosen period and the twelve months before it, with each payer group's typical age and share of cash by age band.",
        "USD");

    Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
