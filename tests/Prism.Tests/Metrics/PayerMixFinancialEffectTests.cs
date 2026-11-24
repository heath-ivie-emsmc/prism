using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Insights;

namespace Prism.Tests.Metrics;

/// <summary>The payer mix effect is worked out in one place, so the Insight and the Metric agree for the same data.</summary>
public sealed class PayerMixFinancialEffectTests
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly PayerCashPerTransport[] Rates =
    [
        new("commercial", "Commercial", 100, 100_000m),
        new("medicaid", "Medicaid", 100, 50_000m),
        new("va", "VA", 0, 0m),
    ];

    private static PayerMixShare[] Shares(int commercial, int commercialPrior, int medicaid, int medicaidPrior) =>
        [new("commercial", "Commercial", commercial, commercialPrior), new("medicaid", "Medicaid", medicaid, medicaidPrior), new("va", "VA", 0, 0)];

    private static PayerMixFinancialEffectMetric Metric(Func<ApPeriod, IReadOnlyList<PayerMixShare>> shares, PayerCashPerTransport[]? rates = null) =>
        new(new InsightFakes.PayerMixFake(shares), new InsightFakes.CashPerTransportFake(rates ?? Rates));

    [Fact]
    public async Task The_metric_and_the_insight_report_the_same_effect_for_the_same_data()
    {
        Func<ApPeriod, IReadOnlyList<PayerMixShare>> shares = p => p == Oct ? Shares(77, 82, 23, 18) : Shares(82, 82, 18, 18);
        var analysis = new PayerMixAnalysis(new InsightFakes.PayerMixFake(shares), new InsightFakes.CashPerTransportFake(Rates));

        Finding? finding = await analysis.RunAsync(new InsightInput(1, Oct, ServiceLineFilter.Air, "fixed-wing", [], []), TestContext.Current.CancellationToken);
        PayerMixFinancialEffect effect = await Metric(shares).GetEffectAsync(1, Oct, ServiceLineFilter.Air, "fixed-wing", null, TestContext.Current.CancellationToken);

        PayerMixFinding insight = Assert.IsType<PayerMixFinding>(finding);
        Assert.Equal(-2_500m, insight.Impact);
        Assert.Equal(insight.Impact, effect.TotalEffect);
    }

    [Fact]
    public async Task Each_group_has_its_share_move_matured_cash_per_transport_and_effect_and_a_group_without_cash_has_none()
    {
        PayerMixFinancialEffect effect = await Metric(p => Shares(77, 82, 23, 18)).GetEffectAsync(1, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("payer_mix_financial_effect", effect.Metric.Key);
        Assert.Equal((100, 100), (effect.Transports, effect.PriorTransports));
        Assert.Equal(
            [("commercial", 1000m, -5_000m), ("medicaid", 500m, 2_500m)],
            effect.Groups.Where(g => g.Effect is not null).Select(g => (g.Share.Key, g.CashPerTransport!.Value, g.Effect!.Value)));
        PayerMixEffectRow va = effect.Groups.Single(g => g.Share.Key == "va");
        Assert.Null(va.CashPerTransport);
        Assert.Null(va.Effect);
        Assert.Equal(new ApPeriod(2025, 5), effect.CohortFrom);
        Assert.Equal(new ApPeriod(2026, 4), effect.CohortThrough);
    }

    [Fact]
    public async Task With_no_transports_in_a_month_there_is_no_effect_and_nothing_is_invented()
    {
        PayerMixFinancialEffect effect = await Metric(p => Shares(10, 0, 5, 0)).GetEffectAsync(1, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Null(effect.TotalEffect);
        Assert.All(effect.Groups, g => Assert.Null(g.Effect));
        Assert.Equal((15, 0), (effect.Transports, effect.PriorTransports));
    }

    [Fact]
    public async Task Without_matured_cash_per_transport_the_total_is_null_not_zero()
    {
        PayerCashPerTransport[] none = [new("commercial", "Commercial", 0, 0m), new("medicaid", "Medicaid", 0, 0m)];

        PayerMixFinancialEffect effect = await Metric(p => Shares(77, 82, 23, 18), none).GetEffectAsync(1, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Null(effect.TotalEffect);
        Assert.All(effect.Groups, g => Assert.Null(g.CashPerTransport));
    }

    [Fact]
    public void The_shared_maths_gives_the_share_move_times_cash_per_transport_times_this_months_transports()
    {
        var mix = new PayerMix(IPayerMixMetric.Definition, Oct, Oct.AddMonths(-1), ServiceLineFilter.Air, null, null, Shares(50, 60, 50, 40));
        var rates = new CashPerTransport(ICashPerTransportMetric.Definition, Oct, Oct.AddMonths(-17), Oct.AddMonths(-6), ServiceLineFilter.Air, null, Rates);

        PayerMixEffectResult result = PayerMixEffect.Compute(mix, rates);

        // Commercial 60% to 50%: -0.10 x 1,000 x 100 = -10,000. Medicaid 40% to 50%: +0.10 x 500 x 100 = +5,000.
        Assert.Equal(-5_000m, result.TotalEffect);
        Assert.Equal((100, 100), (result.Now, result.Before));
    }
}
