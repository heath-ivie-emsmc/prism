using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

public sealed class InsightsEngineTests
{
    private static readonly ApPeriod Oct = new(2026, 10);

    /// <summary>Cash for each month ending in October, oldest first, for one group.</summary>
    private static NetCashByGroupPoint[] Months(string key, params decimal[] cash) =>
        [.. cash.Select((c, i) => new NetCashByGroupPoint(key, key == "fixed-wing" ? "Fixed wing" : "Rotor wing", true, Oct.AddMonths(i - (cash.Length - 1)), c, (int)(c / 1000m)))];

    private static InsightsEngine Engine(IEnumerable<NetCashByGroupPoint> points, params IInsightAnalysis[] analyses) =>
        new(new InsightFakes.NetCashByGroupFake([.. points]), analyses);

    private static Task<Insight?> ExplainAsync(InsightsEngine engine, string? group = "fixed-wing") =>
        engine.ExplainAsync(1, Oct, ServiceLineFilter.Air, group, null, TestContext.Current.CancellationToken);

    private sealed class FixedAnalysis(int order, Finding? finding) : IInsightAnalysis
    {
        public int Order => order;

        public IReadOnlyList<Finding> Seen { get; private set; } = [];

        public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
        {
            Seen = await input.FindingsBeforeAsync();
            return finding;
        }
    }

    /// <summary>Waits until the other analysis has started, which only happens if the engine starts them together.</summary>
    private sealed class Rendezvous(int order, TaskCompletionSource mine, TaskCompletionSource theirs) : IInsightAnalysis
    {
        public int Order => order;

        public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
        {
            mine.SetResult();
            await theirs.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return null;
        }
    }

    private sealed class Failing(int order) : IInsightAnalysis
    {
        public int Order => order;

        public Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken) => Task.FromException<Finding?>(new InvalidOperationException("boom"));
    }

    private sealed class WaitsForCancellation(int order) : IInsightAnalysis
    {
        public bool Cancelled { get; private set; }

        public int Order => order;

        public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            return null;
        }
    }

    private static VolumeFinding Posted(decimal impact) => new(FindingLabel.Watch, impact, 0m, 0m);

    private static PayerMixFinding Expected(decimal impact) => new(FindingLabel.Watch, impact, "medicaid", "Medicaid", 0.18m, 0.23m);

    // Twelve months that move by exactly -/+ 1,000 each month around 100,000, then the two months being compared.
    private static decimal[] Steady(decimal prior, decimal now) =>
        [.. new decimal[] { 100_000, 101_000, 100_000, 101_000, 100_000, 101_000, 100_000, 101_000, 100_000, 101_000, 100_000, prior, now }];

    [Fact]
    public async Task The_headline_is_this_month_against_last_for_the_chosen_group()
    {
        Insight? insight = await ExplainAsync(Engine(Months("fixed-wing", 625_000m, 487_230m).Concat(Months("rotor-wing", 1m, 1m))));

        Assert.NotNull(insight);
        InsightHeadline h = insight.Headline;
        Assert.Equal(("Fixed wing", 487_230m, 625_000m, -137_770m), (h.GroupName, h.CashNow, h.CashPrior, h.Delta));
        Assert.Equal(-0.2204m, Math.Round(h.DeltaRatio!.Value, 4));
        Assert.Equal(new ApPeriod(2026, 9), h.PriorPeriod);
    }

    [Fact]
    public async Task With_no_group_the_groups_are_added_up_and_named_for_the_service_line()
    {
        Insight? insight = await ExplainAsync(Engine(Months("fixed-wing", 600m, 500m).Concat(Months("rotor-wing", 400m, 450m))), group: null);

        Assert.NotNull(insight);
        Assert.Equal(("Air", 950m, 1_000m), (insight.Headline.GroupName, insight.Headline.CashNow, insight.Headline.CashPrior));
    }

    [Fact]
    public async Task Drivers_and_the_remainder_add_up_to_the_change_and_expected_effects_are_left_out_of_the_sum()
    {
        var engine = Engine(Months("fixed-wing", 1_000m, 400m), new FixedAnalysis(1, Posted(-300m)), new FixedAnalysis(2, Expected(-250m)));

        Insight? insight = await ExplainAsync(engine);

        Assert.NotNull(insight);
        Assert.Equal(-600m, insight.Headline.Delta);
        Assert.Equal(-300m, insight.Drivers.Sum(f => f.Impact));
        Assert.Equal(-300m, insight.Remainder.Amount);
        Assert.Equal(-250m, Assert.Single(insight.Expected).Impact);
        Assert.Equal(insight.Headline.Delta, insight.Drivers.Sum(f => f.Impact) + insight.Remainder.Amount);
    }

    [Fact]
    public async Task A_finding_below_the_materiality_bar_is_dropped_and_stays_in_the_remainder()
    {
        // The bar is 10% of 600. 59 is under it and 60 is not.
        Insight? small = await ExplainAsync(Engine(Months("fixed-wing", 1_000m, 400m), new FixedAnalysis(1, Posted(-59m))));
        Insight? exact = await ExplainAsync(Engine(Months("fixed-wing", 1_000m, 400m), new FixedAnalysis(1, Posted(-60m))));

        Assert.Empty(small!.Drivers);
        Assert.Equal(-600m, small.Remainder.Amount);
        Assert.Single(exact!.Drivers);
    }

    [Fact]
    public async Task Findings_are_ranked_by_size_and_capped()
    {
        FixedAnalysis[] analyses = [.. Enumerable.Range(1, 7).Select(i => new FixedAnalysis(i, Posted(-1_000m * i)))];

        Insight? insight = await ExplainAsync(Engine(Months("fixed-wing", 10_000m, 1_000m), analyses));

        Assert.Equal([-7_000m, -6_000m, -5_000m, -4_000m, -3_000m], insight!.Drivers.Select(f => f.Impact));
    }

    [Fact]
    public async Task Analyses_run_in_order_and_each_sees_the_findings_before_it()
    {
        var first = new FixedAnalysis(1, Posted(-300m));
        var second = new FixedAnalysis(2, Posted(-200m));
        var none = new FixedAnalysis(3, null);

        await ExplainAsync(Engine(Months("fixed-wing", 1_000m, 400m), none, second, first));

        Assert.Empty(first.Seen);
        Assert.Equal([-300m], second.Seen.Select(f => f.Impact));
        Assert.Equal([-300m, -200m], none.Seen.Select(f => f.Impact));
    }

    [Fact]
    public async Task Analyses_that_do_not_depend_on_each_other_run_at_the_same_time()
    {
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        // Each waits for the other to have started, so this only finishes if neither waits for the other to finish.
        Insight? insight = await ExplainAsync(Engine(Months("fixed-wing", 1_000m, 400m), new Rendezvous(1, a, b), new Rendezvous(2, b, a)));

        Assert.NotNull(insight);
    }

    [Fact]
    public async Task A_failing_analysis_cancels_the_rest_and_its_failure_is_what_the_caller_sees()
    {
        var slow = new WaitsForCancellation(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExplainAsync(Engine(Months("fixed-wing", 1_000m, 400m), new Failing(1), slow)));

        Assert.True(slow.Cancelled);
    }

    [Fact]
    public async Task No_cash_for_either_month_is_no_insight_and_no_change_means_no_findings()
    {
        Assert.Null(await ExplainAsync(Engine(Months("rotor-wing", 1m, 1m))));

        Insight? flat = await ExplainAsync(Engine(Months("fixed-wing", 1_000m, 1_000m), new FixedAnalysis(1, Posted(-100m))));
        Assert.Empty(flat!.Drivers);
    }

    [Fact]
    public async Task A_remainder_inside_the_usual_range_is_normal_and_one_outside_it_is_not()
    {
        // The twelve months move by +/-1,000, so the usual range is about -91 to +91 around a mean near 0 (the changes average 91).
        Insight? inside = await ExplainAsync(Engine(Months("fixed-wing", Steady(100_000m, 100_050m))));
        Insight? outside = await ExplainAsync(Engine(Months("fixed-wing", Steady(100_000m, 90_000m))));

        Assert.Equal(RemainderJudgment.WithinNormalRange, inside!.Remainder.Judgment);
        Assert.Equal(RemainderJudgment.OutsideNormalRange, outside!.Remainder.Judgment);
    }

    [Fact]
    public async Task Fewer_than_twelve_months_of_history_says_nothing_about_normal()
    {
        Insight? shortHistory = await ExplainAsync(Engine(Months("fixed-wing", 100_000m, 101_000m, 100_000m, 101_000m, 100_000m, 100_050m)));

        Assert.Equal(RemainderJudgment.NotEnoughHistory, shortHistory!.Remainder.Judgment);
    }

    [Fact]
    public async Task A_gap_in_the_twelve_months_is_not_enough_history()
    {
        NetCashByGroupPoint[] withGap = [.. Months("fixed-wing", Steady(100_000m, 100_050m)).Where(p => p.Period != Oct.AddMonths(-5))];

        Insight? insight = await ExplainAsync(Engine(withGap));

        Assert.Equal(RemainderJudgment.NotEnoughHistory, insight!.Remainder.Judgment);
    }

    [Fact]
    public async Task The_normalized_change_takes_a_batch_of_aged_payments_out_of_both_months()
    {
        // September 625,000 including an 85,000 batch, October 487,230. Without the batch: 487,230 against 540,000, about -9.8%.
        var aged = new AgedPaymentsFinding(-85_000m, new ApPeriod(2026, 9), 0m, 85_000m, 0m, 14m, 14);

        Insight? insight = await ExplainAsync(Engine(Months("fixed-wing", 625_000m, 487_230m), new FixedAnalysis(1, aged)));

        Assert.Equal(-0.0977m, Math.Round(insight!.NormalizedDeltaRatio!.Value, 4));
        Assert.Null((await ExplainAsync(Engine(Months("fixed-wing", 625_000m, 487_230m))))!.NormalizedDeltaRatio);
    }
}
