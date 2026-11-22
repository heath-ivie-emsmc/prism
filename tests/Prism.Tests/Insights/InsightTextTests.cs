using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

public sealed class InsightTextTests
{
    private static readonly ApPeriod Oct = new(2026, 10);
    private static readonly ApPeriod Sep = new(2026, 9);

    private static Insight Make(
        decimal delta,
        decimal prior,
        Finding[]? drivers = null,
        Finding[]? expected = null,
        decimal remainder = 0m,
        RemainderJudgment judgment = RemainderJudgment.WithinNormalRange,
        decimal? normalized = null) =>
        new(
            new InsightHeadline(Oct, Sep, "fixed-wing", "Fixed wing", prior + delta, prior, delta, prior == 0m ? null : delta / prior),
            drivers ?? [],
            expected ?? [],
            new Remainder(remainder, judgment),
            normalized);

    [Fact]
    public void The_headline_says_how_much_cash_fell_and_that_most_of_it_is_timing_when_temporary_drivers_are_over_half()
    {
        var aged = new AgedPaymentsFinding(-85_000m, Sep, 0m, 85_000m, 0m, 14m, 14);

        InsightNarrative text = InsightText.Build(Make(-137_770m, 625_000m, [aged]));

        Assert.Equal("Fixed wing cash fell $137,770.00 (22%) from September. Most of it is timing, not a lasting change.", text.Headline);
    }

    [Fact]
    public void The_headline_does_not_claim_timing_when_temporary_drivers_are_under_half_or_go_the_other_way()
    {
        var small = new AgedPaymentsFinding(-60_000m, Sep, 0m, 60_000m, 0m, 5m, 5);
        var opposite = new AgedPaymentsFinding(90_000m, Oct, 90_000m, 0m, 5m, 0m, 5);

        Assert.Equal("Fixed wing cash fell $137,770.00 (22%) from September.", InsightText.Build(Make(-137_770m, 625_000m, [small])).Headline);
        Assert.DoesNotContain("timing", InsightText.Build(Make(-137_770m, 625_000m, [opposite])).Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void The_headline_handles_a_rise_no_change_and_no_prior_cash()
    {
        Assert.Equal("Fixed wing cash rose $5,000.00 (10%) from September.", InsightText.Build(Make(5_000m, 50_000m)).Headline);
        Assert.Equal("Fixed wing cash was unchanged from September.", InsightText.Build(Make(0m, 50_000m)).Headline);
        Assert.Equal("Fixed wing cash rose $5,000.00 from September.", InsightText.Build(Make(5_000m, 0m)).Headline);
    }

    [Fact]
    public void An_aged_payments_card_gives_the_excess_the_batch_and_the_normalized_change()
    {
        var aged = new AgedPaymentsFinding(-85_000m, Sep, 0m, 85_000m, 0m, 14m, 14);

        InsightCard card = Assert.Single(InsightText.Build(Make(-137_770m, 625_000m, [aged], normalized: -0.0977m)).Cards);

        Assert.Equal("September had unusually high payments on older transports", card.Title);
        Assert.Equal(
            "Payments on older transports in September ran $85,000.00 above the usual level, across 14 transports. Without that, fixed wing cash fell about 10%, not 22%.",
            card.Body);
        Assert.Equal((FindingLabel.Temporary, FindingBasis.PostedCash, -85_000m), (card.Label, card.Basis, card.Impact));
    }

    [Fact]
    public void A_payer_mix_card_says_it_is_an_expected_effect()
    {
        var mix = new PayerMixFinding(FindingLabel.Watch, -38_400m, "medicaid", "Medicaid", 0.18m, 0.23m);

        InsightCard card = Assert.Single(InsightText.Build(Make(-137_770m, 625_000m, expected: [mix])).Cards);

        Assert.Equal("More fixed wing transports were Medicaid", card.Title);
        Assert.Contains("Medicaid's share of fixed wing transports rose from 18% to 23%", card.Body, StringComparison.Ordinal);
        Assert.Contains("about $38,400.00 lower a month", card.Body, StringComparison.Ordinal);
        Assert.Contains("expected effect, not part of what moved this month's cash", card.Body, StringComparison.Ordinal);
        Assert.Equal(FindingBasis.ExpectedEffect, card.Basis);
    }

    [Fact]
    public void A_volume_card_gives_the_transports_and_the_dollars()
    {
        var volume = new VolumeFinding(FindingLabel.Watch, -20_000m, 100m, 80m);

        InsightCard card = Assert.Single(InsightText.Build(Make(-50_000m, 500_000m, [volume])).Cards);

        Assert.Equal("Fewer transports had cash posted", card.Title);
        Assert.Contains("went from 100 to 80", card.Body, StringComparison.Ordinal);
        Assert.Contains("$20,000.00", card.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RemainderJudgment.WithinNormalRange, "The remaining $14,370.00 is normal month-to-month variation.")]
    [InlineData(RemainderJudgment.OutsideNormalRange, "The remaining $14,370.00 is not explained by the drivers we can measure.")]
    [InlineData(RemainderJudgment.NotEnoughHistory, "The remaining $14,370.00 is not explained by the drivers we can measure.")]
    public void The_remainder_line_calls_it_normal_only_inside_the_usual_range(RemainderJudgment judgment, string expected)
    {
        Assert.Equal(expected, InsightText.Build(Make(-14_370m, 100_000m, remainder: -14_370m, judgment: judgment)).RemainderLine);
    }

    [Fact]
    public void Cards_list_the_drivers_first_and_then_the_expected_effects()
    {
        var driver = new VolumeFinding(FindingLabel.Watch, -20_000m, 100m, 80m);
        var expected = new PayerMixFinding(FindingLabel.Watch, -38_400m, "medicaid", "Medicaid", 0.18m, 0.23m);

        InsightNarrative text = InsightText.Build(Make(-50_000m, 500_000m, [driver], [expected]));

        Assert.Equal([FindingBasis.PostedCash, FindingBasis.ExpectedEffect], text.Cards.Select(c => c.Basis));
    }
}
