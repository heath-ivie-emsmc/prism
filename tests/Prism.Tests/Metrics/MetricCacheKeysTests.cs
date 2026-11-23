using System.Globalization;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Tests.Metrics;

/// <summary>The text that tells one answer of a Metric from another: no two different lists of values may give the same text.</summary>
public sealed class MetricCacheKeysTests
{
    [Fact]
    public void Values_that_would_run_together_under_a_separator_stay_apart()
    {
        Assert.NotEqual(MetricCacheKeys.Of("a|b", "c"), MetricCacheKeys.Of("a", "b|c"));
        Assert.NotEqual(MetricCacheKeys.Of("a;1:b", "c"), MetricCacheKeys.Of("a", "b", "c"));
        Assert.NotEqual(MetricCacheKeys.Of("ab", "c"), MetricCacheKeys.Of("a", "bc"));
        Assert.NotEqual(MetricCacheKeys.Of("1:a;"), MetricCacheKeys.Of("a"));
    }

    [Fact]
    public void Nothing_empty_and_a_missing_value_are_all_different()
    {
        Assert.NotEqual(MetricCacheKeys.Of(null, "x"), MetricCacheKeys.Of("", "x"));
        Assert.NotEqual(MetricCacheKeys.Of((object?)null), MetricCacheKeys.Of("~"));
        Assert.NotEqual(MetricCacheKeys.Of(), MetricCacheKeys.Of(""));
        Assert.NotEqual(MetricCacheKeys.Of("x", null), MetricCacheKeys.Of("x"));
    }

    [Fact]
    public void The_same_values_always_give_the_same_text_whatever_the_current_culture()
    {
        var period = new ApPeriod(2026, 10);
        string expected = MetricCacheKeys.Of(period, 6, ServiceLineFilter.Air, "fixed-wing", null);
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal(expected, MetricCacheKeys.Of(period, 6, ServiceLineFilter.Air, "fixed-wing", null));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_different_service_line_period_or_months_is_a_different_key()
    {
        var period = new ApPeriod(2026, 10);

        Assert.NotEqual(MetricCacheKeys.Of(period, ServiceLineFilter.Air), MetricCacheKeys.Of(period, ServiceLineFilter.Ground));
        Assert.NotEqual(MetricCacheKeys.Of(period, 6), MetricCacheKeys.Of(period, 12));
        Assert.NotEqual(MetricCacheKeys.Of(period, 6), MetricCacheKeys.Of(period.AddMonths(-1), 6));
    }
}
