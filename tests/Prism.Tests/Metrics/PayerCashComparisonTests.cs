using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

public sealed class PayerCashComparisonTests
{
    [Fact]
    public void The_difference_and_percent_are_measured_from_the_overall_figure()
    {
        var payer = new PayerCashPerTransport("commercial", "Commercial", 2, 500m);

        Assert.Equal(50m, payer.DifferenceFrom(200m));
        Assert.Equal(25.0m, payer.PercentFrom(200m));
        Assert.Equal(-37.5m, new PayerCashPerTransport("medicare", "Medicare", 1, 100m).PercentFrom(160m));
    }

    [Theory]
    [InlineData(0)]
    public void A_zero_overall_figure_has_a_difference_but_no_percent(int overall)
    {
        var payer = new PayerCashPerTransport("commercial", "Commercial", 2, 500m);

        Assert.Equal(250m, payer.DifferenceFrom(overall));
        Assert.Null(payer.PercentFrom(overall));
    }

    [Fact]
    public void A_missing_overall_figure_or_a_group_without_transports_has_neither()
    {
        var payer = new PayerCashPerTransport("commercial", "Commercial", 2, 500m);
        var empty = new PayerCashPerTransport("va", "VA", 0, 0m);

        Assert.Null(payer.DifferenceFrom(null));
        Assert.Null(payer.PercentFrom(null));
        Assert.Null(empty.DifferenceFrom(100m));
        Assert.Null(empty.PercentFrom(100m));
    }
}
