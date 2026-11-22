using Prism.Application.SavedViews;

namespace Prism.Tests.SavedViews;

/// <summary>The layout validator is the gate between a browser and the database. Each rule has a positive and an adversarial case.</summary>
public sealed class LayoutValidatorTests
{
    private static LayoutWidget Widget(string id = "w1", string metric = "net_collections", string presentation = "line", int x = 0, int y = 0, int w = 6, int h = 3) =>
        new(id, metric, presentation, null, x, y, w, h);

    private static SavedViewLayout Layout(params LayoutWidget[] widgets) => new(1, "comfortable", widgets);

    [Fact]
    public void A_good_layout_has_no_problems()
    {
        SavedViewLayout layout = Layout(
            Widget("a") with { Options = new Dictionary<string, string> { ["months"] = "12" } },
            Widget("b", "payer_mix", "donut", 6, 0, 3, 3),
            Widget("c", "clean_claim_rate", "tile", 9, 0, 3, 2));

        Assert.Empty(LayoutValidator.Validate(layout));
    }

    [Fact]
    public void An_empty_layout_is_fine()
    {
        Assert.Empty(LayoutValidator.Validate(Layout()));
        Assert.Empty(LayoutValidator.Validate(new SavedViewLayout(1, null, null)));
    }

    [Fact]
    public void A_missing_layout_is_a_problem() => Assert.Contains(LayoutValidator.BadVersion, LayoutValidator.Validate(null));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void An_unknown_version_is_refused(int version) =>
        Assert.Contains(LayoutValidator.BadVersion, LayoutValidator.Validate(new SavedViewLayout(version, "compact", [])));

    [Theory]
    [InlineData("Compact")]
    [InlineData("")]
    [InlineData("huge")]
    public void An_unknown_density_is_refused(string density) =>
        Assert.Contains(LayoutValidator.BadDensity, LayoutValidator.Validate(new SavedViewLayout(1, density, [])));

    [Fact]
    public void More_than_the_most_widgets_is_refused_without_looking_at_each_one()
    {
        LayoutWidget[] many = [.. Enumerable.Range(0, SavedViewLimits.MaxWidgets + 1).Select(i => Widget($"w{i}", "no_such_metric"))];

        IReadOnlyList<string> problems = LayoutValidator.Validate(Layout(many));

        Assert.Equal([LayoutValidator.TooManyWidgets], problems);
    }

    [Fact]
    public void Exactly_the_most_widgets_is_allowed()
    {
        // Twelve 1-high tiles on two rows would not fit the bounds, so use the smallest legal tile: 3 wide, 2 high, four across, six down.
        LayoutWidget[] widgets = [.. Enumerable.Range(0, SavedViewLimits.MaxWidgets).Select(i => Widget($"w{i}", "clean_claim_rate", "tile", i % 4 * 3, i / 4 * 2, 3, 2))];

        Assert.Empty(LayoutValidator.Validate(Layout(widgets)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("W1")]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("a_b")]
    public void A_widget_id_is_letters_digits_and_hyphens_only(string id) =>
        Assert.Contains(LayoutValidator.BadWidgetId, LayoutValidator.Validate(Layout(Widget(id))));

    [Fact]
    public void A_widget_id_over_the_length_cap_is_refused() =>
        Assert.Contains(LayoutValidator.BadWidgetId, LayoutValidator.Validate(Layout(Widget(new string('a', SavedViewLimits.MaxWidgetIdLength + 1)))));

    [Fact]
    public void Two_widgets_cannot_share_an_id() =>
        Assert.Contains(LayoutValidator.DuplicateWidgetId, LayoutValidator.Validate(Layout(Widget("a"), Widget("a", x: 6))));

    [Theory]
    [InlineData("nope")]
    [InlineData("NET_COLLECTIONS")]
    [InlineData("")]
    [InlineData("net_collections; DROP TABLE saved_view")]
    public void A_metric_the_catalog_does_not_have_is_refused(string key) =>
        Assert.Contains(LayoutValidator.UnknownMetric, LayoutValidator.Validate(Layout(Widget(metric: key))));

    [Theory]
    [InlineData("ar_days")]
    [InlineData("denied_claims")]
    public void A_planned_metric_cannot_be_placed(string key) =>
        Assert.Contains(LayoutValidator.MetricNotPlaceable, LayoutValidator.Validate(Layout(Widget(metric: key, presentation: "table"))));

    [Fact]
    public void The_dashboard_only_insight_cannot_be_placed() =>
        Assert.Contains(LayoutValidator.MetricNotPlaceable, LayoutValidator.Validate(Layout(Widget(metric: "what_changed"))));

    [Fact]
    public void A_presentation_the_metric_does_not_offer_is_refused() =>
        Assert.Contains(LayoutValidator.PresentationNotAllowed, LayoutValidator.Validate(Layout(Widget(presentation: "donut"))));

    [Theory]
    [InlineData(6, 3, 0, 0, false)]
    [InlineData(5, 3, 0, 0, true)]
    [InlineData(13, 3, 0, 0, true)]
    [InlineData(6, 2, 0, 0, true)]
    [InlineData(6, 7, 0, 0, true)]
    public void A_size_outside_the_presentations_bounds_is_refused(int w, int h, int x, int y, bool refused)
    {
        IReadOnlyList<string> problems = LayoutValidator.Validate(Layout(Widget(x: x, y: y, w: w, h: h)));

        Assert.Equal(refused, problems.Contains(LayoutValidator.SizeOutOfBounds));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(7, 0)]
    [InlineData(0, 198)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    public void A_widget_outside_the_grid_is_refused(int x, int y) =>
        Assert.Contains(LayoutValidator.OutsideGrid, LayoutValidator.Validate(Layout(Widget(x: x, y: y))));

    [Fact]
    public void Overlapping_widgets_are_refused_and_touching_ones_are_not()
    {
        Assert.Contains(LayoutValidator.Overlap, LayoutValidator.Validate(Layout(Widget("a"), Widget("b", x: 3, y: 1))));
        Assert.Empty(LayoutValidator.Validate(Layout(Widget("a"), Widget("b", x: 6), Widget("c", y: 3))));
    }

    [Theory]
    [InlineData("months", "7", LayoutValidator.OptionValueInvalid)]
    [InlineData("months", "", LayoutValidator.OptionValueInvalid)]
    [InlineData("nope", "6", LayoutValidator.OptionNotAllowed)]
    public void An_option_the_catalog_does_not_allow_or_with_a_bad_value_is_refused(string key, string value, string code) =>
        Assert.Contains(code, LayoutValidator.Validate(Layout(Widget() with { Options = new Dictionary<string, string> { [key] = value } })));

    [Fact]
    public void An_option_that_does_not_apply_to_the_presentation_is_refused()
    {
        // Months apply to the Metric's chart presentations and its table: a donut of payer mix has none.
        Assert.Contains(LayoutValidator.OptionNotAllowed, LayoutValidator.Validate(Layout(Widget(metric: "payer_mix", presentation: "donut", w: 3) with { Options = new Dictionary<string, string> { ["months"] = "6" } })));
    }

    [Fact]
    public void Too_many_options_on_one_widget_are_refused()
    {
        Dictionary<string, string> options = Enumerable.Range(0, SavedViewLimits.MaxOptionsPerWidget + 1).ToDictionary(i => $"k{i}", _ => "v");

        Assert.Contains(LayoutValidator.OptionNotAllowed, LayoutValidator.Validate(Layout(Widget() with { Options = options })));
    }

    [Fact]
    public void A_problem_never_quotes_what_was_sent()
    {
        const string marker = "PHI-MARKER-Jane-Doe-1970-01-01";
        SavedViewLayout layout = new(1, marker, [Widget(marker, marker, marker) with { Options = new Dictionary<string, string> { [marker] = marker } }]);

        IReadOnlyList<string> problems = LayoutValidator.Validate(layout);

        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.DoesNotContain("MARKER", problem, StringComparison.Ordinal));
    }
}
