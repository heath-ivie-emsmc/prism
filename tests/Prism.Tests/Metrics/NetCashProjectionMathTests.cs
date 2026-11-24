using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

public sealed class NetCashProjectionMathTests
{
    private static readonly NetCashProjectionMonth[] Months =
    [
        new(new ApPeriod(2026, 11), 100m, 80m, 130m),
        new(new ApPeriod(2026, 12), 150m, 120m, 160m),
        new(new ApPeriod(2027, 1), 75m, 70m, 90m),
    ];

    [Fact]
    public void The_first_month_is_compared_with_the_latest_actual_and_later_months_with_the_month_before()
    {
        NetCashProjectionView view = NetCashProjectionMath.Build(Months, 3, 80m);

        Assert.Equal(new AmountChange(20m, 25.0m), view.Steps[0].ChangeFromPrevious);
        Assert.Equal(new AmountChange(50m, 50.0m), view.Steps[1].ChangeFromPrevious);
        Assert.Equal(new AmountChange(-75m, -50.0m), view.Steps[2].ChangeFromPrevious);
    }

    [Fact]
    public void Without_an_actual_month_the_first_change_is_null()
    {
        NetCashProjectionView view = NetCashProjectionMath.Build(Months, 3, null);

        Assert.Null(view.Steps[0].ChangeFromPrevious);
        Assert.NotNull(view.Steps[1].ChangeFromPrevious);
    }

    [Fact]
    public void A_zero_base_has_an_amount_but_no_percent()
    {
        NetCashProjectionView view = NetCashProjectionMath.Build(Months, 1, 0m);

        Assert.Equal(new AmountChange(100m, null), view.Steps[0].ChangeFromPrevious);
    }

    [Fact]
    public void Cumulative_and_horizon_totals_cover_only_the_months_within_the_horizon()
    {
        NetCashProjectionView two = NetCashProjectionMath.Build(Months, 2, null);

        Assert.Equal(new RunningTotal(250m, 200m, 290m), two.Horizon);
        Assert.Equal(new RunningTotal(100m, 80m, 130m), two.Steps[0].Cumulative);
        Assert.Equal(two.Horizon, two.Steps[1].Cumulative);
        Assert.Equal(2, two.Steps.Count);
    }

    [Fact]
    public void No_months_gives_zero_totals()
    {
        NetCashProjectionView view = NetCashProjectionMath.Build([], 3, 10m);

        Assert.Empty(view.Steps);
        Assert.Equal(new RunningTotal(0m, 0m, 0m), view.Horizon);
    }

    [Fact]
    public void Components_are_summed_over_the_horizon_with_shares_of_the_value_and_null_when_missing_or_the_value_is_zero()
    {
        NetCashProjectionMonth[] months =
        [
            new(new ApPeriod(2026, 11), 100m, 80m, 130m, 90m, 130m),
            new(new ApPeriod(2026, 12), 100m, 80m, 130m, 60m, null),
            new(new ApPeriod(2027, 1), 0m, 0m, 0m, 5m, 5m),
        ];

        NetCashProjectionView two = NetCashProjectionMath.Build(months, 2, null);
        NetCashProjectionView three = NetCashProjectionMath.Build(months, 3, null);

        Assert.Equal(new ProjectionComponents(90m, 130m, 0.9m, 1.3m), two.Steps[0].Components);
        Assert.Equal(new ProjectionComponents(60m, null, 0.6m, null), two.Steps[1].Components);
        Assert.Equal(new ProjectionComponents(150m, 130m, 0.75m, 0.65m), two.HorizonComponents);
        Assert.Equal(new ProjectionComponents(5m, 5m, null, null), three.Steps[2].Components);
        Assert.Equal(new ProjectionComponents(null, null, null, null), NetCashProjectionMath.Build([new(new ApPeriod(2026, 11), 10m, 1m, 2m)], 1, null).HorizonComponents);
    }
}
