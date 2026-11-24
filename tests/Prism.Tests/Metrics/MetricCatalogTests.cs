using System.Reflection;
using System.Text.Json;
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
    public void Every_metric_the_api_serves_has_an_available_catalog_entry_that_is_placeable_unless_it_is_a_drill_target()
    {
        HashSet<string> drillTargets = [.. MetricCatalog.Entries.SelectMany(e => e.Drills).Select(d => d.TargetKey)];
        List<MetricDefinition> served = ServedMetrics();

        Assert.NotEmpty(served);
        foreach (MetricDefinition definition in served)
        {
            MetricCatalogEntry? entry = MetricCatalog.Find(definition.Key);
            Assert.NotNull(entry);
            Assert.Equal(MetricStatus.Available, entry.Status);
            // A drill target with nothing to draw is reached by a link from another Metric and never placed on a Saved View. One that has its own presentations (payer mix) is both.
            Assert.Equal(!(drillTargets.Contains(definition.Key) && entry.Presentations.Count == 0), entry.CanBePlaced);
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

    private static readonly string[] DrawableKinds = ["line", "column", "stacked", "donut", "table", "tile"];

    [Fact]
    public void Presentations_are_only_kinds_the_widgets_can_draw_and_every_metric_can_be_read_as_a_table()
    {
        foreach (MetricCatalogEntry entry in MetricCatalog.Entries.Where(e => e.CanBePlaced))
        {
            Assert.All(entry.Presentations, p => Assert.Contains(p.Key, DrawableKinds));
            Assert.Contains(entry.Presentations, p => p.Key == "table");
            Assert.All(entry.Presentations, p => Assert.False(string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Icon)));
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

    // Metrics that honor the Reporting Division filter, and the integration test class that proves the filter narrows their figures against a real PostgreSQL.
    // A Metric may list the division dimension, or say DivisionSupport.Supported, only if it is here and its test exists.
    private static readonly Dictionary<string, string> DivisionCoverage = new(StringComparer.Ordinal)
    {
        ["payer_mix"] = "PayerMixMetricTests",
        ["payer_mix_detail"] = "PayerMixDetailMetricTests",
        ["transport_volume"] = "TransportVolumeMetricTests",
        ["payment_age"] = "PaymentAgeMetricTests",
        ["cash_per_transport_by_payer"] = "CashPerTransportMetricTests",
        ["net_cash_breakdown"] = "NetCashBreakdownMetricTests",
        ["net_cash_by_group"] = "NetCashByGroupMetricTests",
        ["transport_mix_by_call_type"] = "TransportMixByCallTypeMetricTests",
        ["aircraft_performance"] = "AircraftPerformanceMetricTests",
        ["base_analysis"] = "BaseAnalysisMetricTests",
        ["loaded_miles_by_aircraft"] = "LoadedMilesByAircraftMetricTests",
        ["payer_mix_financial_effect"] = "PayerMixFinancialEffectMetricTests",
        ["transports_by_pickup_location"] = "TransportsByPickupLocationMetricTests",
        ["cash_contribution_by_aircraft"] = "CashContributionMetricTests",
        ["cash_contribution_by_division"] = "CashContributionMetricTests",
        ["cash_contribution_by_payer"] = "CashContributionMetricTests",
        ["net_collections"] = "NetCollectionsMetricTests",
        ["what_changed"] = "WhatChangedDivisionTests",
    };

    [Fact]
    public void A_metric_claims_the_division_filter_only_when_an_integration_test_proves_it()
    {
        string[] claimed = [.. MetricCatalog.Entries.Where(e => e.Dimensions.Contains(MetricDimensions.Division) || e.Context.DivisionSupport == DivisionSupport.Supported).Select(e => e.Key).Order(StringComparer.Ordinal)];

        Assert.Equal([.. DivisionCoverage.Keys.Order(StringComparer.Ordinal)], claimed);
        Assert.All(MetricCatalog.Entries, e => Assert.Equal(e.Dimensions.Contains(MetricDimensions.Division), e.Context.DivisionSupport == DivisionSupport.Supported));
        foreach ((string key, string testClass) in DivisionCoverage)
        {
            string path = Path.Combine(Architecture.RepoFiles.Root, "tests", "Prism.Tests.Integration", "Metrics", testClass + ".cs");
            Assert.True(File.Exists(path), $"{key} claims the division filter but {testClass} does not exist.");
            string source = File.ReadAllText(path);
            Assert.Contains("A_division_narrows_the_figures", source, StringComparison.Ordinal);
            Assert.Contains("A_division_name_that_is_not_the_providers_matches_nothing", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Metrics_whose_tables_have_no_division_column_do_not_claim_the_division_filter()
    {
        // The forecast table (reporting.forecast_cash_per_trip) and the clean claim table (reporting.kpi_clean_claim_rate__prov, split by pod only) have no Reporting Division
        // column and no known link from a pod to a division, so they cannot be split honestly. See docs/warehouse.md.
        string[] unsupported = ["net_cash_projection", "clean_claim_rate"];

        Assert.All(unsupported, key =>
        {
            MetricCatalogEntry entry = MetricCatalog.Find(key)!;
            Assert.DoesNotContain(MetricDimensions.Division, entry.Dimensions);
            Assert.Equal(DivisionSupport.None, entry.Context.DivisionSupport);
        });
    }

    [Fact]
    public void Every_available_metric_has_a_help_key_equal_to_its_key()
    {
        Assert.All(MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Available), e => Assert.Equal(e.Key, e.HelpKey));
        Assert.All(MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Planned), e => Assert.Empty(e.HelpKey));
    }

    [Fact]
    public void Freshness_is_no_cache_exactly_for_claim_level_metrics()
    {
        Assert.All(MetricCatalog.Entries, e =>
            Assert.Equal(e.ClaimLevel ? FreshnessPolicy.NoCache : FreshnessPolicy.DailyLoad, e.Freshness.Policy));
    }

    [Fact]
    public void Every_metric_is_provider_scoped()
    {
        Assert.All(MetricCatalog.Entries, e => Assert.True(e.Context.ProviderScoped));
    }

    [Fact]
    public void Drills_target_an_available_entry_and_bind_one_of_its_dimensions()
    {
        MetricCatalogEntry[] withDrills = [.. MetricCatalog.Entries.Where(e => e.Drills.Count > 0)];

        Assert.NotEmpty(withDrills);
        foreach (MetricDrill drill in withDrills.SelectMany(e => e.Drills))
        {
            MetricCatalogEntry? target = MetricCatalog.Find(drill.TargetKey);
            Assert.NotNull(target);
            Assert.Equal(MetricStatus.Available, target.Status);
            Assert.False(string.IsNullOrWhiteSpace(drill.Rel));
            Assert.Contains(drill.BindsDimension, target.Dimensions);
        }
    }

    [Fact]
    public void The_payer_mix_drills_to_the_payer_detail_by_payer_group()
    {
        MetricDrill drill = Assert.Single(MetricCatalog.Find("payer_mix")!.Drills);

        Assert.Equal(new MetricDrill("drill", "payer_mix_detail", "payerGroup"), drill);
        MetricCatalogEntry target = MetricCatalog.Find("payer_mix_detail")!;
        Assert.Equal(("payer", "transports", DivisionSupport.Supported, FreshnessPolicy.DailyLoad, "payer_mix_detail"), (target.Category, target.Unit, target.Context.DivisionSupport, target.Freshness.Policy, target.HelpKey));
        Assert.Empty(target.Presentations);
        Assert.Equal(["air", "ground", "both"], target.ServiceLines);
    }

    [Fact]
    public void The_breakdown_drills_to_the_payer_mix_by_group()
    {
        MetricDrill drill = Assert.Single(MetricCatalog.Find("net_cash_breakdown")!.Drills);

        Assert.Equal(new MetricDrill("drill", "payer_mix", "group"), drill);
        MetricCatalogEntry target = MetricCatalog.Find("payer_mix")!;
        Assert.Equal(MetricStatus.Available, target.Status);
        Assert.Contains(MetricDimensions.Group, target.Dimensions);
        Assert.Contains(MetricDimensions.ServiceLine, target.Dimensions);
        Assert.Equal(["air", "ground", "both"], target.ServiceLines);
    }

    [Fact]
    public void A_drill_chain_is_at_most_three_metrics_long()
    {
        Assert.Equal(3, Depth("net_cash_breakdown"));

        static int Depth(string key) => 1 + MetricCatalog.Find(key)!.Drills.Select(d => Depth(d.TargetKey)).DefaultIfEmpty(0).Max();
    }

    [Fact]
    public void Drills_form_no_cycle()
    {
        foreach (MetricCatalogEntry entry in MetricCatalog.Entries)
        {
            Assert.False(Reaches(entry.Key, entry.Key, 0), $"{entry.Key} drills back to itself.");
        }

        static bool Reaches(string from, string goal, int depth)
        {
            Assert.True(depth < 5, "Drills chain deeper than a handful of steps.");
            return MetricCatalog.Find(from)!.Drills.Any(d => d.TargetKey == goal || Reaches(d.TargetKey, goal, depth + 1));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NET_COLLECTIONS")]
    [InlineData("net_collections ")]
    [InlineData("nope")]
    public void Find_does_not_match_a_key_loosely(string? key) => Assert.Null(MetricCatalog.Find(key));
}
