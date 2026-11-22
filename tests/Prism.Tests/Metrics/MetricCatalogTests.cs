using System.Reflection;
using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

/// <summary>The catalog is the one place a Saved View learns what it may show, so it has to agree with the Metrics the API actually serves.</summary>
public sealed class MetricCatalogTests
{
    private static readonly string[] ValidSizes = ["S", "M", "L"];
    private static readonly string[] ToggleValues = ["true", "false"];

    /// <summary>Every Metric interface in the application assembly that exposes a static <c>Definition</c>.</summary>
    private static List<MetricDefinition> ServedMetrics() =>
        [.. typeof(MetricDefinition).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Metric", StringComparison.Ordinal))
            .Select(t => t.GetProperty("Definition", BindingFlags.Public | BindingFlags.Static)?.GetValue(null))
            .OfType<MetricDefinition>()];

    [Fact]
    public void Every_metric_the_api_serves_has_an_available_placeable_catalog_entry()
    {
        List<MetricDefinition> served = ServedMetrics();

        Assert.NotEmpty(served);
        foreach (MetricDefinition definition in served)
        {
            MetricCatalogEntry? entry = MetricCatalog.Find(definition.Key);
            Assert.NotNull(entry);
            Assert.Equal(MetricStatus.Available, entry.Status);
            Assert.True(entry.CanBePlaced, definition.Key);
            Assert.Equal(definition.Name, entry.Name);
            Assert.Equal(definition.Unit, entry.Unit);
        }
    }

    [Fact]
    public void Keys_are_unique_and_use_only_lowercase_letters_digits_and_underscores()
    {
        string[] keys = [.. MetricCatalog.Entries.Select(e => e.Key)];

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.Matches("^[a-z][a-z0-9_]{1,63}$", key));
    }

    [Fact]
    public void Every_entry_belongs_to_a_known_category()
    {
        HashSet<string> categories = [.. MetricCatalog.Categories.Select(c => c.Key)];

        Assert.All(MetricCatalog.Entries, entry => Assert.Contains(entry.Category, categories));
    }

    [Fact]
    public void A_planned_metric_cannot_be_placed_and_has_nothing_to_draw()
    {
        IEnumerable<MetricCatalogEntry> planned = MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Planned);

        Assert.NotEmpty(planned);
        Assert.All(planned, entry =>
        {
            Assert.False(entry.CanBePlaced, entry.Key);
            Assert.Empty(entry.Presentations);
        });
    }

    [Fact]
    public void A_claim_level_metric_is_never_available_until_it_has_a_phi_permission_and_an_audit_record()
    {
        Assert.Contains(MetricCatalog.Entries, e => e.ClaimLevel);
        Assert.All(MetricCatalog.Entries.Where(e => e.ClaimLevel), entry => Assert.Equal(MetricStatus.Planned, entry.Status));
    }

    [Fact]
    public void The_dashboard_only_insight_is_not_placeable()
    {
        MetricCatalogEntry? entry = MetricCatalog.Find("what_changed");

        Assert.NotNull(entry);
        Assert.False(entry.CanBePlaced);
    }

    [Fact]
    public void Every_placeable_metric_has_exactly_one_default_presentation_with_sane_bounds()
    {
        foreach (MetricCatalogEntry entry in MetricCatalog.Entries.Where(e => e.CanBePlaced))
        {
            Assert.Single(entry.Presentations, p => p.IsDefault);
            Assert.Equal(entry.Presentations.Count, entry.Presentations.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count());
            foreach (WidgetPresentation p in entry.Presentations)
            {
                Assert.InRange(p.MinWidth, 1, 12);
                Assert.InRange(p.MaxWidth, p.MinWidth, 12);
                Assert.InRange(p.MinHeight, 1, p.MaxHeight);
                Assert.NotEmpty(p.Sizes);
                Assert.All(p.Sizes, size => Assert.Contains(size, ValidSizes));
            }
        }
    }

    [Fact]
    public void Options_only_apply_to_presentations_the_metric_has_and_their_defaults_are_valid()
    {
        foreach (MetricCatalogEntry entry in MetricCatalog.Entries)
        {
            HashSet<string> presentations = [.. entry.Presentations.Select(p => p.Key)];
            foreach (WidgetOption option in entry.Options)
            {
                Assert.All(option.AppliesTo, key => Assert.Contains(key, presentations));
                switch (option.Type)
                {
                    case "choice":
                        Assert.Contains(option.Default, option.Choices);
                        break;
                    case "toggle":
                        Assert.Contains(option.Default, ToggleValues);
                        break;
                    default:
                        Assert.Fail($"{entry.Key}.{option.Key} has the type '{option.Type}' that the settings form cannot draw.");
                        break;
                }
            }
        }
    }

    [Fact]
    public void No_metric_claims_to_honor_the_reporting_division_filter_yet()
    {
        // The Reporting Division filter reaches no Metric query today. When one does, list it here and drop the widget's "not filtered" note.
        Assert.All(MetricCatalog.Entries, entry => Assert.DoesNotContain(MetricDimensions.Division, entry.Dimensions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NET_COLLECTIONS")]
    [InlineData("net_collections ")]
    [InlineData("nope")]
    public void Find_does_not_match_a_key_loosely(string? key) => Assert.Null(MetricCatalog.Find(key));
}
