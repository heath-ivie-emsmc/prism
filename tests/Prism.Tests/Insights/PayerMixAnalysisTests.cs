using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

public sealed class PayerMixAnalysisTests
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly PayerCashPerTransport[] Rates =
    [
        new("commercial", "Commercial", 100, 100_000m),
        new("medicaid", "Medicaid", 100, 50_000m),
        new("va", "VA", 0, 0m),
    ];

    private static InsightInput Input() => new(1, Oct, ServiceLineFilter.Air, "fixed-wing", [], []);

    private static PayerMixShare[] Shares(int commercial, int commercialPrior, int medicaid, int medicaidPrior) =>
        [new("commercial", "Commercial", commercial, commercialPrior), new("medicaid", "Medicaid", medicaid, medicaidPrior)];

    private static async Task<(Finding? Finding, InsightFakes.PayerMixFake Mix)> RunAsync(Func<ApPeriod, IReadOnlyList<PayerMixShare>> shares, PayerCashPerTransport[]? rates = null)
    {
        var mix = new InsightFakes.PayerMixFake(shares);
        var analysis = new PayerMixAnalysis(mix, new InsightFakes.CashPerTransportFake(rates ?? Rates));
        return (await analysis.RunAsync(Input(), TestContext.Current.CancellationToken), mix);
    }

    [Fact]
    public async Task A_shift_toward_a_lower_paying_payer_is_an_expected_negative_effect_named_for_the_group_that_rose()
    {
        // Medicaid 18% to 23% of 100 transports. A commercial transport earns 1,000 and a Medicaid one 500, so five transports move: -500 each.
        (Finding? finding, _) = await RunAsync(p => p == Oct ? Shares(77, 82, 23, 18) : Shares(82, 82, 18, 18));

        PayerMixFinding mix = Assert.IsType<PayerMixFinding>(finding);
        Assert.Equal(("medicaid", "Medicaid", 0.18m, 0.23m), (mix.PayerKey, mix.PayerName, mix.ShareFrom, mix.ShareTo));
        Assert.Equal(-2_500m, mix.Impact);
        Assert.Equal(FindingBasis.ExpectedEffect, mix.Basis);
    }

    [Fact]
    public async Task It_is_structural_when_the_same_group_also_rose_the_month_before_and_watch_when_it_did_not()
    {
        (Finding? held, _) = await RunAsync(p => p == Oct ? Shares(77, 82, 23, 18) : Shares(82, 87, 18, 13));
        (Finding? fresh, _) = await RunAsync(p => p == Oct ? Shares(77, 82, 23, 18) : Shares(82, 82, 18, 18));

        Assert.Equal(FindingLabel.Structural, Assert.IsType<PayerMixFinding>(held).Label);
        Assert.Equal(FindingLabel.Watch, Assert.IsType<PayerMixFinding>(fresh).Label);
    }

    [Fact]
    public async Task A_shift_toward_a_higher_paying_payer_is_for_information_and_asks_nothing_more()
    {
        (Finding? finding, InsightFakes.PayerMixFake mix) = await RunAsync(p => Shares(82, 77, 18, 23));

        PayerMixFinding found = Assert.IsType<PayerMixFinding>(finding);
        Assert.Equal((FindingLabel.Info, 2_500m, "commercial"), (found.Label, found.Impact, found.PayerKey));
        Assert.Equal([Oct], mix.Calls);
    }

    [Fact]
    public async Task A_small_move_empty_months_and_unpriced_payers_say_nothing()
    {
        Assert.Null((await RunAsync(p => Shares(800, 805, 200, 195))).Finding);
        Assert.Null((await RunAsync(p => Shares(0, 0, 0, 0))).Finding);
        Assert.Null((await RunAsync(p => Shares(77, 82, 23, 18), [new PayerCashPerTransport("commercial", "Commercial", 0, 0m)])).Finding);
    }
}
