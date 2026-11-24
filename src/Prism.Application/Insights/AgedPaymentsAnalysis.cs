using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>
/// Finds payments on older transports that ran above the usual level, in this month or the one before. For each payer group, "older" means older than
/// the age by which 90% of that group's transports are normally paid, over the months before the two being compared, so slow payers are not flagged for
/// being slow. The usual level is the average of that group's older payments over the six months before the two being compared.
/// Only unusually high payments count, and a batch needs a few transports so one late claim is not a story.
/// </summary>
public sealed class AgedPaymentsAnalysis(IPaymentAgeMetric paymentAge) : IInsightAnalysis
{
    public int Order => 10;

    public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
    {
        PaymentAge age = await paymentAge.GetPaymentAgeAsync(input.ProviderId, input.Period, input.ServiceLine, input.GroupKey, input.Division, cancellationToken);
        ApPeriod prior = input.PriorPeriod;
        ApPeriod[] baselineMonths = [.. Enumerable.Range(2, InsightThresholds.BaselineMonths).Select(i => input.Period.AddMonths(-i))];

        Excess now = default;
        Excess before = default;
        foreach (IGrouping<string, PaymentAgeCell> payer in age.Cells.GroupBy(c => c.PayerKey))
        {
            PaymentAgeCell[] history = [.. payer.Where(c => c.PostingPeriod != input.Period && c.PostingPeriod != prior)];
            if (history.Sum(c => c.Transports) < InsightThresholds.MinBaselineTransports)
            {
                continue;
            }

            int usualAge = UsualAge(history);
            decimal usualLevel = baselineMonths.Sum(m => Aged(history, m, usualAge).Cash) / baselineMonths.Length;
            now = now.Plus(ExcessOf(payer, input.Period, usualAge, usualLevel));
            before = before.Plus(ExcessOf(payer, prior, usualAge, usualLevel));
        }

        if (Math.Max(now.Transports, before.Transports) < InsightThresholds.MinBatchTransports || (now.Cash == 0m && before.Cash == 0m))
        {
            return null;
        }

        return new AgedPaymentsFinding(
            now.Cash - before.Cash,
            before.Cash > now.Cash ? prior : input.Period,
            now.Cash,
            before.Cash,
            now.ExcessTransports,
            before.ExcessTransports,
            (int)Math.Max(now.Transports, before.Transports));
    }

    /// <summary>The smallest age (in months) by which the usual share of transports have been paid.</summary>
    public static int UsualAge(IReadOnlyCollection<PaymentAgeCell> history)
    {
        decimal total = history.Sum(c => c.Transports);
        decimal running = 0m;
        foreach (IGrouping<int, PaymentAgeCell> byAge in history.GroupBy(c => c.AgeMonths).OrderBy(g => g.Key))
        {
            running += byAge.Sum(c => c.Transports);
            if (running >= total * InsightThresholds.UsualAgePercentile)
            {
                return byAge.Key;
            }
        }

        return IPaymentAgeMetric.MaxAgeMonths;
    }

    private static (decimal Cash, decimal Transports) Aged(IEnumerable<PaymentAgeCell> cells, ApPeriod month, int usualAge)
    {
        PaymentAgeCell[] older = [.. cells.Where(c => c.PostingPeriod == month && c.AgeMonths > usualAge)];
        return (older.Sum(c => c.Cash), older.Sum(c => c.Transports));
    }

    private static Excess ExcessOf(IEnumerable<PaymentAgeCell> cells, ApPeriod month, int usualAge, decimal usualLevel)
    {
        (decimal cash, decimal transports) = Aged(cells, month, usualAge);
        decimal excess = Math.Max(0m, cash - usualLevel);
        return excess == 0m ? default : new Excess(excess, transports, transports * excess / cash);
    }

    /// <summary>The unusually high aged cash in one month, the transports behind it, and the part of those transports that the excess stands for.</summary>
    private readonly record struct Excess(decimal Cash, decimal Transports, decimal ExcessTransports)
    {
        public Excess Plus(Excess other) => new(Cash + other.Cash, Transports + other.Transports, ExcessTransports + other.ExcessTransports);
    }
}
