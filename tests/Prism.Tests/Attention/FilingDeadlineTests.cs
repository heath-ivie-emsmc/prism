using Prism.Application.Attention;

namespace Prism.Tests.Attention;

public sealed class FilingDeadlineTests
{
    private static readonly DateOnly DataThrough = new(2026, 11, 21);

    [Fact]
    public void The_due_date_is_the_service_date_plus_the_limit()
    {
        Assert.Equal(new DateOnly(2027, 10, 2), FilingDeadline.DueDate(new DateOnly(2026, 10, 2), 365));
    }

    [Theory]
    [InlineData("2026-11-21", 0)]
    [InlineData("2026-11-22", 1)]
    [InlineData("2026-11-20", -1)]
    [InlineData("2026-12-21", 30)]
    public void Days_left_count_from_data_through_not_from_today(string due, int expected)
    {
        Assert.Equal(expected, FilingDeadline.DaysLeft(DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), DataThrough));
    }

    [Fact]
    public void A_payer_category_uses_its_own_limit_ignoring_case_and_any_other_uses_the_default()
    {
        var options = new FilingLimitOptions { Default = 365, ByPayerCategory = new() { ["Medicare"] = 90 } };

        Assert.Equal(90, options.LimitFor("medicare"));
        Assert.Equal(365, options.LimitFor("Insurance"));
        Assert.Equal(365, options.LimitFor(null));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1825, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1826, false)]
    public void A_limit_must_be_between_one_day_and_five_years(int days, bool valid)
    {
        Assert.Equal(valid, new FilingLimitOptions { Default = days }.IsValid());
        Assert.Equal(valid, new FilingLimitOptions { ByPayerCategory = new() { ["Medicare"] = days } }.IsValid());
    }

    [Fact]
    public void A_payer_category_is_found_whatever_its_case_even_when_a_plain_dictionary_was_assigned()
    {
        var plain = new Dictionary<string, int> { ["Medicare"] = 90 };
        var options = new FilingLimitOptions { Default = 365, ByPayerCategory = plain };

        Assert.Equal(90, options.LimitFor("MEDICARE"));
        Assert.Equal(90, options.LimitFor("medicare"));
        Assert.Equal(365, options.LimitFor("Commercial"));
        Assert.Equal(365, options.LimitFor(null));
    }
}
