using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

public sealed class AgedPaymentsAnalysisTests
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static InsightInput Input() => new(1, Oct, ServiceLineFilter.Air, "fixed-wing", [], []);

    /// <summary>Ten transports a month paid at age 3 for every month of the window except the ones in <paramref name="skip"/>.</summary>
    private static IEnumerable<PaymentAgeCell> Steady(string payer, params ApPeriod[] skip) =>
        Enumerable.Range(0, 13).Select(i => Oct.AddMonths(-i)).Where(m => !skip.Contains(m))
            .Select(m => new PaymentAgeCell(payer, payer, m, 3, 10_000m, 10));

    private static Task<Finding?> RunAsync(IEnumerable<PaymentAgeCell> cells) =>
        new AgedPaymentsAnalysis(new InsightFakes.PaymentAgeFake([.. cells])).RunAsync(Input(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_batch_of_old_payments_last_month_shows_as_a_temporary_fall_this_month()
    {
        PaymentAgeCell batch = new("commercial", "Commercial", new ApPeriod(2026, 9), 8, 85_000m, 14);

        Finding? finding = await RunAsync([.. Steady("commercial"), batch]);

        AgedPaymentsFinding aged = Assert.IsType<AgedPaymentsFinding>(finding);
        Assert.Equal(-85_000m, aged.Impact);
        Assert.Equal(new ApPeriod(2026, 9), aged.InflatedPeriod);
        Assert.Equal((0m, 85_000m, 14), (aged.ExcessThisMonth, aged.ExcessPriorMonth, aged.BatchTransports));
        Assert.Equal(14m, aged.ExcessTransportsPriorMonth);
        Assert.Equal((FindingLabel.Temporary, FindingBasis.PostedCash), (aged.Label, aged.Basis));
    }

    [Fact]
    public async Task A_batch_this_month_is_a_positive_impact_in_this_month()
    {
        PaymentAgeCell batch = new("commercial", "Commercial", Oct, 8, 40_000m, 5);

        Finding? finding = await RunAsync([.. Steady("commercial"), batch]);

        AgedPaymentsFinding aged = Assert.IsType<AgedPaymentsFinding>(finding);
        Assert.Equal(40_000m, aged.Impact);
        Assert.Equal(Oct, aged.InflatedPeriod);
    }

    [Fact]
    public async Task A_slow_payer_that_always_pays_late_is_not_flagged()
    {
        // Half its transports are paid at age 4 and half at age 9, every month, so 9 months is simply how long this payer takes.
        IEnumerable<PaymentAgeCell> slow = Enumerable.Range(0, 13).Select(i => Oct.AddMonths(-i))
            .SelectMany(m => new[] { new PaymentAgeCell("commercial", "Commercial", m, 4, 5_000m, 5), new PaymentAgeCell("commercial", "Commercial", m, 9, 20_000m, 5) });

        Assert.Null(await RunAsync(slow));
    }

    [Fact]
    public async Task Old_payments_at_the_usual_level_are_not_an_excess()
    {
        // Every month, in the baseline and the two compared, has the same 4 transports paid at age 12 on top of the steady ones.
        IEnumerable<PaymentAgeCell> usual = Enumerable.Range(0, 13).Select(i => new PaymentAgeCell("commercial", "Commercial", Oct.AddMonths(-i), 12, 8_000m, 4));

        Assert.Null(await RunAsync([.. Steady("commercial"), .. usual]));
    }

    [Fact]
    public async Task A_batch_smaller_than_the_minimum_is_not_a_finding()
    {
        PaymentAgeCell late = new("commercial", "Commercial", new ApPeriod(2026, 9), 8, 85_000m, 2);

        Assert.Null(await RunAsync([.. Steady("commercial"), late]));
    }

    [Fact]
    public async Task A_payer_with_too_little_history_is_skipped()
    {
        PaymentAgeCell[] thin =
        [
            new("va", "VA", new ApPeriod(2026, 8), 2, 1_000m, 3),
            new("va", "VA", new ApPeriod(2026, 9), 9, 85_000m, 5),
        ];

        Assert.Null(await RunAsync(thin));
    }

    [Fact]
    public async Task Excess_is_added_up_across_payers()
    {
        PaymentAgeCell[] batches =
        [
            new("commercial", "Commercial", new ApPeriod(2026, 9), 8, 30_000m, 4),
            new("medicaid", "Medicaid", new ApPeriod(2026, 9), 8, 20_000m, 3),
        ];

        Finding? finding = await RunAsync([.. Steady("commercial"), .. Steady("medicaid"), .. batches]);

        AgedPaymentsFinding aged = Assert.IsType<AgedPaymentsFinding>(finding);
        Assert.Equal(-50_000m, aged.Impact);
        Assert.Equal(7, aged.BatchTransports);
    }

    [Theory]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 10 }, 10)]
    [InlineData(new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }, 9)]
    public void The_usual_age_is_the_age_by_which_nine_in_ten_transports_are_paid(int[] transportsByAge, int expectedAge)
    {
        // One cell for each age from 1 month, with the given transports.
        PaymentAgeCell[] cells = [.. transportsByAge.Select((t, i) => new PaymentAgeCell("commercial", "Commercial", Oct, i + 1, 100m, t))];

        Assert.Equal(expectedAge, AgedPaymentsAnalysis.UsualAge(cells));
    }
}
