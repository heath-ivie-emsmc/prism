using System.Security.Claims;
using Prism.Api.Metrics;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Infrastructure.Auth;

namespace Prism.Tests.Metrics;

/// <summary>The links a payer mix row carries are issued from the catalog: same Provider as the route, only filters in effect, and left out whenever the target would refuse.</summary>
public sealed class DrillLinkBuilderTests
{
    private static readonly MetricCatalogEntry PayerMix = MetricCatalog.Find("payer_mix")!;

    private static ClaimsPrincipal User(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(p => new Claim(SessionTokenIssuer.PermissionsClaim, p)), "test"));

    private static DrillLinkBuilder Builder(DrillContext context, string provider = "100", ClaimsPrincipal? user = null, Func<string, MetricCatalogEntry?>? find = null) =>
        new(PayerMix, user ?? User(Permissions.App, Permissions.DashboardView), provider, context, find);

    private static readonly DrillContext Everything = new("202610", ServiceLineFilter.Air, "fixed-wing", "rotor-base");

    [Theory]
    [InlineData("commercial", "Commercial")]
    [InlineData("medicare", "Medicare")]
    [InlineData("medicaid", "Medicaid")]
    [InlineData("other-gov", "Other government")]
    [InlineData("va", "VA")]
    public void A_payer_group_with_detail_gets_one_drill_link_with_every_filter_in_effect(string key, string name)
    {
        LinkResponse link = Assert.Single(Builder(Everything).LinksFor(key, name));

        Assert.Equal("drill", link.Rel);
        Assert.Equal($"/api/v1/providers/100/metrics/net-collections/payer-mix/{key}?group=fixed-wing&period=202610&serviceLine=air&division=rotor-base", link.Href);
        Assert.Equal($"Payer mix detail: {name}", link.Title);
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("facility")]
    [InlineData("not-recorded")]
    [InlineData("")]
    [InlineData("Commercial")]
    [InlineData("commercial/../x")]
    [InlineData("nope")]
    public void A_value_the_target_refuses_gets_no_link(string key) =>
        Assert.Empty(Builder(Everything).LinksFor(key, "Whatever"));

    [Fact]
    public void Only_the_filters_in_effect_are_in_the_query()
    {
        LinkResponse link = Assert.Single(Builder(new DrillContext("202610", ServiceLineFilter.Both, null, null)).LinksFor("va", "VA"));

        Assert.Equal("/api/v1/providers/100/metrics/net-collections/payer-mix/va?period=202610&serviceLine=all", link.Href);
    }

    [Fact]
    public void Values_are_url_encoded()
    {
        LinkResponse link = Assert.Single(Builder(new DrillContext("2026 10&x=1", ServiceLineFilter.Air, "a/b?c", "d#e")).LinksFor("va", "VA"));

        Assert.Equal("/api/v1/providers/100/metrics/net-collections/payer-mix/va?group=a%2Fb%3Fc&period=2026%2010%26x%3D1&serviceLine=air&division=d%23e", link.Href);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("a/../../b")]
    public void The_href_names_the_route_provider_and_nothing_else(string routeProvider)
    {
        LinkResponse link = Assert.Single(Builder(Everything, routeProvider).LinksFor("va", "VA"));

        Assert.StartsWith($"/api/v1/providers/{Uri.EscapeDataString(routeProvider)}/metrics/", link.Href, StringComparison.Ordinal);
        // The id stays one path segment: no slash from it reaches the path.
        Assert.Equal(9, link.Href.Split('?')[0].Split('/').Length);
    }

    [Fact]
    public void A_caller_without_the_targets_permission_gets_no_links() =>
        Assert.Empty(Builder(Everything, user: User(Permissions.App)).LinksFor("va", "VA"));

    [Fact]
    public void A_target_that_does_not_serve_the_service_line_gets_no_links()
    {
        MetricCatalogEntry airOnly = MetricCatalog.Find("payer_mix_detail")! with { ServiceLines = ["air"] };
        Func<string, MetricCatalogEntry?> find = key => key == "payer_mix_detail" ? airOnly : MetricCatalog.Find(key);

        Assert.Single(Builder(Everything, find: find).LinksFor("va", "VA"));
        Assert.Empty(Builder(Everything with { ServiceLine = ServiceLineFilter.Ground }, find: find).LinksFor("va", "VA"));
    }

    [Theory]
    [InlineData(MetricStatus.Planned)]
    public void A_target_that_is_not_available_or_is_missing_gets_no_links(MetricStatus status)
    {
        MetricCatalogEntry planned = MetricCatalog.Find("payer_mix_detail")! with { Status = status };

        Assert.Empty(Builder(Everything, find: _ => planned).LinksFor("va", "VA"));
        Assert.Empty(Builder(Everything, find: _ => null).LinksFor("va", "VA"));
    }

    [Fact]
    public void A_drill_that_binds_a_dimension_the_target_lacks_gets_no_link()
    {
        MetricCatalogEntry noPayerGroup = MetricCatalog.Find("payer_mix_detail")! with { Dimensions = ["period"] };

        Assert.Empty(Builder(Everything, find: _ => noPayerGroup).LinksFor("va", "VA"));
    }

    [Fact]
    public void A_target_without_a_route_gets_no_link_and_links_are_not_followed_from_the_target()
    {
        MetricCatalogEntry unrouted = PayerMix with { Drills = [new MetricDrill("drill", "net_collections", "period")] };
        var builder = new DrillLinkBuilder(unrouted, User(Permissions.DashboardView), "100", Everything);

        Assert.Empty(builder.LinksFor("va", "VA"));
        Assert.Empty(MetricCatalog.Find("payer_mix_detail")!.Drills);
    }

    [Fact]
    public void A_source_without_drills_gets_no_links()
    {
        var builder = new DrillLinkBuilder(MetricCatalog.Find("transport_volume")!, User(Permissions.DashboardView), "100", Everything);

        Assert.Empty(builder.LinksFor("va", "VA"));
    }

    private static readonly MetricCatalogEntry Breakdown = MetricCatalog.Find("net_cash_breakdown")!;

    private static DrillLinkBuilder BreakdownBuilder(DrillContext context, ClaimsPrincipal? user = null, string provider = "100") =>
        new(Breakdown, user ?? User(Permissions.App, Permissions.DashboardView), provider, context);

    [Fact]
    public void A_breakdown_group_links_to_the_payer_mix_with_its_key_as_the_group_and_every_filter_in_effect()
    {
        LinkResponse link = Assert.Single(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, null, "rotor-base")).LinksFor("fixed-wing", "Fixed wing"));

        Assert.Equal("drill", link.Rel);
        Assert.Equal("/api/v1/providers/100/metrics/net-collections/payer-mix?group=fixed-wing&period=202610&serviceLine=air&division=rotor-base", link.Href);
        Assert.Equal("Payer mix: Fixed wing", link.Title);
    }

    [Fact]
    public void A_breakdown_link_has_only_the_filters_in_effect()
    {
        LinkResponse link = Assert.Single(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Both, null, null)).LinksFor("bls", "BLS"));

        Assert.Equal("/api/v1/providers/100/metrics/net-collections/payer-mix?group=bls&period=202610&serviceLine=all", link.Href);
    }

    [Fact]
    public void The_clicked_group_replaces_a_group_filter_already_in_effect_and_is_url_encoded()
    {
        Assert.Equal(
            "/api/v1/providers/100/metrics/net-collections/payer-mix?group=ground&period=202610&serviceLine=air",
            Assert.Single(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, "fixed-wing", null)).LinksFor("ground", "Ground")).Href);
        Assert.Empty(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, null, null)).LinksFor("a/b?c", "x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Fixed-Wing")]
    [InlineData("fixed wing")]
    [InlineData("fixed-wing&division=x")]
    [InlineData("../x")]
    public void A_breakdown_group_key_the_payer_mix_would_refuse_gets_no_link(string key) =>
        Assert.Empty(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, null, null)).LinksFor(key, "Whatever"));

    [Fact]
    public void A_breakdown_link_is_left_out_without_the_dashboard_permission()
    {
        Assert.Empty(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, null, null), User(Permissions.App)).LinksFor("fixed-wing", "Fixed wing"));
    }

    [Fact]
    public void A_breakdown_link_names_the_route_provider_and_is_left_out_when_the_target_lacks_the_group_dimension()
    {
        LinkResponse link = Assert.Single(BreakdownBuilder(new DrillContext("202610", ServiceLineFilter.Air, null, null), provider: "101").LinksFor("fixed-wing", "Fixed wing"));
        MetricCatalogEntry noGroup = MetricCatalog.Find("payer_mix")! with { Dimensions = ["period"] };

        Assert.StartsWith("/api/v1/providers/101/", link.Href, StringComparison.Ordinal);
        Assert.Empty(new DrillLinkBuilder(Breakdown, User(Permissions.DashboardView), "100", Everything, _ => noGroup).LinksFor("fixed-wing", "Fixed wing"));
    }

    [Fact]
    public void A_breakdown_link_is_left_out_for_a_service_line_the_target_does_not_serve()
    {
        MetricCatalogEntry airOnly = MetricCatalog.Find("payer_mix")! with { ServiceLines = ["air"] };

        Assert.Empty(new DrillLinkBuilder(Breakdown, User(Permissions.DashboardView), "100", new DrillContext("202610", ServiceLineFilter.Ground, null, null), _ => airOnly).LinksFor("bls", "BLS"));
    }
}
