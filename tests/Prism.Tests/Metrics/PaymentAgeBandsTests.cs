using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

public sealed class PaymentAgeBandsTests
{
    private static readonly ApPeriod Posting = new(2026, 10);

    private static PaymentAgeCell Cell(int age, decimal cash, string payer = "commercial") => new(payer, payer, Posting, age, cash, 1);

    [Fact]
    public void The_bands_are_defined_once_and_cover_every_age_without_a_gap()
    {
        Assert.Equal(["0-1", "2-3", "4-6", "7-12", "13+"], PaymentAgeBands.All.Select(b => b.Key));
        Assert.Equal(0, PaymentAgeBands.All[0].FromMonths);
        for (int i = 1; i < PaymentAgeBands.All.Count; i++)
        {
            Assert.Equal(PaymentAgeBands.All[i - 1].ToMonths + 1, PaymentAgeBands.All[i].FromMonths);
        }

        Assert.Null(PaymentAgeBands.All[^1].ToMonths);
    }

    [Theory]
    [InlineData(0, "0-1")]
    [InlineData(1, "0-1")]
    [InlineData(2, "2-3")]
    [InlineData(3, "2-3")]
    [InlineData(4, "4-6")]
    [InlineData(6, "4-6")]
    [InlineData(7, "7-12")]
    [InlineData(12, "7-12")]
    [InlineData(13, "13+")]
    [InlineData(24, "13+")]
    public void An_age_falls_in_exactly_one_band_at_each_boundary(int age, string band)
    {
        PayerPaymentAge payer = Assert.Single(PaymentAgeBands.Summarize([Cell(age, 100m)]));

        Assert.Equal(band, Assert.Single(payer.Bands, b => b.Share == 1m).Band.Key);
        Assert.Equal(age, payer.TypicalAgeMonths);
    }

    [Fact]
    public void The_typical_age_is_the_cash_weighted_median_and_the_shares_sum_to_one()
    {
        // 100 at age 1, 100 at age 3, 300 at age 8: half of 500 is reached at age 8.
        PayerPaymentAge payer = Assert.Single(PaymentAgeBands.Summarize([Cell(8, 300m), Cell(1, 100m), Cell(3, 100m)]));

        Assert.Equal(8, payer.TypicalAgeMonths);
        Assert.Equal([0.2m, 0.2m, 0m, 0.6m, 0m], payer.Bands.Select(b => b.Share));
        Assert.Equal(1m, payer.Bands.Sum(b => b.Share));
    }

    [Fact]
    public void Exactly_half_of_the_cash_by_an_age_makes_that_age_the_median()
    {
        PayerPaymentAge payer = Assert.Single(PaymentAgeBands.Summarize([Cell(1, 100m), Cell(5, 100m)]));

        Assert.Equal(1, payer.TypicalAgeMonths);
    }

    [Fact]
    public void Shares_sum_to_one_within_rounding_for_awkward_amounts()
    {
        PayerPaymentAge payer = Assert.Single(PaymentAgeBands.Summarize([Cell(0, 1m), Cell(2, 1m), Cell(5, 1m), Cell(9, 1m), Cell(14, 1m), Cell(15, 1m), Cell(16, 1m)]));

        Assert.InRange(payer.Bands.Sum(b => b.Share), 0.9996m, 1.0004m);
    }

    [Fact]
    public void A_payer_with_no_cash_or_only_refunds_has_no_typical_age_and_no_bands()
    {
        IReadOnlyList<PayerPaymentAge> payers = PaymentAgeBands.Summarize([Cell(3, 0m, "va"), Cell(4, -50m, "medicaid")]);

        Assert.Equal(["medicaid", "va"], payers.Select(p => p.PayerKey));
        Assert.All(payers, p =>
        {
            Assert.Null(p.TypicalAgeMonths);
            Assert.Empty(p.Bands);
        });
    }

    [Fact]
    public void Refund_cells_do_not_move_the_median_or_push_a_share_out_of_range()
    {
        PayerPaymentAge payer = Assert.Single(PaymentAgeBands.Summarize([Cell(2, 200m), Cell(20, -500m)]));

        Assert.Equal(2, payer.TypicalAgeMonths);
        Assert.Equal(1m, payer.Bands.Single(b => b.Band.Key == "2-3").Share);
    }

    [Fact]
    public void Each_payer_group_is_summarised_on_its_own_in_a_fixed_order()
    {
        IReadOnlyList<PayerPaymentAge> payers = PaymentAgeBands.Summarize([Cell(10, 100m, "medicare"), Cell(1, 100m, "commercial")]);

        Assert.Equal([("commercial", 1), ("medicare", 10)], payers.Select(p => (p.PayerKey, p.TypicalAgeMonths ?? -1)));
    }
}
