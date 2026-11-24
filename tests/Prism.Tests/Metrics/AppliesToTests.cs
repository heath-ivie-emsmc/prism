using System.Security.Claims;
using Prism.Api.Metrics;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Infrastructure.Auth;

namespace Prism.Tests.Metrics;

/// <summary>
/// What a Metric is about: air only, ground only or both. The rule: <c>both</c> lists air, ground and both in <c>ServiceLines</c>, <c>air</c> lists air and both,
/// <c>ground</c> lists ground and both. For an air-only Metric <c>serviceLine=all</c> means air and <c>ground</c> is refused (symmetric for ground only).
/// </summary>
public sealed class AppliesToTests
{
    [Theory]
    [InlineData(MetricAppliesTo.Both, ServiceLineFilter.Both, ServiceLineFilter.Both)]
    [InlineData(MetricAppliesTo.Both, ServiceLineFilter.Air, ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Both, ServiceLineFilter.Ground, ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Air, ServiceLineFilter.Both, ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Air, ServiceLineFilter.Air, ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Air, ServiceLineFilter.Ground, null)]
    [InlineData(MetricAppliesTo.Ground, ServiceLineFilter.Both, ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Ground, ServiceLineFilter.Ground, ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Ground, ServiceLineFilter.Air, null)]
    public void The_service_line_in_effect_follows_what_the_metric_is_about(MetricAppliesTo appliesTo, ServiceLineFilter asked, ServiceLineFilter? expected) =>
        Assert.Equal(expected, ServiceLineScope.Effective(appliesTo, asked));

    [Fact]
    public void The_lines_a_metric_lists_follow_from_what_it_is_about()
    {
        Assert.Equal(["air", "ground", "both"], ServiceLineScope.LinesFor(MetricAppliesTo.Both));
        Assert.Equal(["air", "both"], ServiceLineScope.LinesFor(MetricAppliesTo.Air));
        Assert.Equal(["ground", "both"], ServiceLineScope.LinesFor(MetricAppliesTo.Ground));
    }

    [Fact]
    public void Every_available_entry_lists_the_service_lines_its_applies_to_implies_and_planned_ones_apply_to_both()
    {
        foreach (MetricCatalogEntry entry in MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Available))
        {
            Assert.Equal(ServiceLineScope.LinesFor(entry.Context.AppliesTo), entry.ServiceLines);
            Assert.Equal(entry.ServiceLines, entry.Context.ServiceLines);
        }

        Assert.All(MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Planned), e => Assert.Equal(MetricAppliesTo.Both, e.Context.AppliesTo));
    }

    [Fact]
    public void The_aircraft_metrics_are_air_only_and_every_other_metric_is_both()
    {
        string[] airOnly = ["aircraft_performance", "loaded_miles_by_aircraft", "cash_contribution_by_aircraft"];

        foreach (MetricCatalogEntry entry in MetricCatalog.Entries)
        {
            Assert.Equal(airOnly.Contains(entry.Key) ? MetricAppliesTo.Air : MetricAppliesTo.Both, entry.Context.AppliesTo);
        }

        Assert.Equal(MetricAppliesTo.Both, MetricCatalog.Find("clean_claim_rate")!.Context.AppliesTo);
    }

    private static MetricCatalogEntry Target(MetricAppliesTo appliesTo) =>
        MetricCatalog.Find("payer_mix_detail")! with { Context = MetricCatalog.Find("payer_mix_detail")!.Context with { AppliesTo = appliesTo, ServiceLines = ServiceLineScope.LinesFor(appliesTo) } };

    [Theory]
    [InlineData(ServiceLineFilter.Air, true)]
    [InlineData(ServiceLineFilter.Both, true)]
    [InlineData(ServiceLineFilter.Ground, false)]
    public void A_drill_to_an_air_only_target_is_offered_for_air_and_all_and_left_out_for_ground(ServiceLineFilter line, bool offered)
    {
        MetricCatalogEntry airOnly = Target(MetricAppliesTo.Air) with { ServiceLines = ServiceLineScope.LinesFor(MetricAppliesTo.Air) };
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionTokenIssuer.PermissionsClaim, Permissions.DashboardView)], "test"));
        var builder = new DrillLinkBuilder(MetricCatalog.Find("payer_mix")!, user, "100", new DrillContext("202610", line, null, null), key => key == "payer_mix_detail" ? airOnly : null);

        Assert.Equal(offered ? 1 : 0, builder.LinksFor("commercial", "Commercial").Count);
    }
}
