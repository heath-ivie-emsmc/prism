using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Prism.Api.Admin;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Tests.Admin;

public sealed class InternalAccessTests
{
    [Theory]
    [InlineData("EMSbilling.com Users", true)]
    [InlineData("  emsbilling.com users ", true)]
    [InlineData("EMSbilling.com User", false)]
    [InlineData("Testing group", false)]
    public void Membership_matches_the_configured_group_name_exactly_ignoring_case_and_spaces(string userGroup, bool expected)
    {
        Assert.Equal(expected, InternalAccess.IsInternal([userGroup], "EMSbilling.com Users"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_configured_group_matches_nobody_even_a_blank_group(string configured)
    {
        Assert.False(InternalAccess.IsInternal(["", "   ", "EMSbilling.com Users"], configured));
    }

    [Fact]
    public void The_marker_role_makes_a_user_internal_without_any_group()
    {
        Assert.True(InternalAccess.IsInternal([], "", [Permissions.App, Permissions.InternalUser]));
        Assert.True(InternalAccess.IsInternal([], "EMSbilling.com Users", [Permissions.InternalUser]));
    }

    [Fact]
    public void A_tier_alone_does_not_make_a_user_internal()
    {
        Assert.False(InternalAccess.IsInternal([], "EMSbilling.com Users", [Permissions.App, Permissions.AdminManage, Permissions.DashboardView]));
    }

    [Fact]
    public void An_admin_tier_is_dropped_for_a_user_outside_the_group()
    {
        string[] permissions = [Permissions.App, Permissions.AdminTemplatesPublish, Permissions.DashboardView];

        Assert.Equal([Permissions.App, Permissions.DashboardView], InternalAccess.Filter(permissions, isInternal: false));
        Assert.Equal(permissions, InternalAccess.Filter(permissions, isInternal: true));
    }

    [Fact]
    public void Groups_are_read_from_names_or_objects_and_anything_else_gives_none()
    {
        using JsonDocument names = JsonDocument.Parse("""{"groups":["EMSbilling.com Users","Testing group","Testing group"]}""");
        using JsonDocument objects = JsonDocument.Parse("""{"user_groups":[{"id":1,"name":"EMSbilling.com Users"},{"id":2},7,null]}""");
        using JsonDocument unknown = JsonDocument.Parse("""{"groups":"EMSbilling.com Users"}""");
        using JsonDocument missing = JsonDocument.Parse("""{"username":"jdoe"}""");

        Assert.Equal(["EMSbilling.com Users", "Testing group"], PortalDirectory.ReadGroups(names.RootElement));
        Assert.Equal(["EMSbilling.com Users"], PortalDirectory.ReadGroups(objects.RootElement));
        Assert.Empty(PortalDirectory.ReadGroups(unknown.RootElement));
        Assert.Empty(PortalDirectory.ReadGroups(missing.RootElement));
    }

    [Fact]
    public void Group_names_are_capped_in_count_and_length()
    {
        string many = string.Join(",", Enumerable.Range(0, 300).Select(i => $"\"g{i}\""));
        string longName = new('x', PortalDirectory.MaxNameLength + 1);
        using JsonDocument manyDoc = JsonDocument.Parse($$"""{"groups":[{{many}}]}""");
        using JsonDocument longDoc = JsonDocument.Parse($$"""{"groups":["{{longName}}"]}""");

        Assert.Equal(PortalDirectory.MaxGroups, PortalDirectory.ReadGroups(manyDoc.RootElement).Count);
        Assert.Empty(PortalDirectory.ReadGroups(longDoc.RootElement));
    }

    [Theory]
    [InlineData(true, new[] { Permissions.AdminTemplatesPublish }, Permissions.AdminTemplatesPublish, true)]
    [InlineData(true, new[] { Permissions.AdminTemplatesAuthor }, Permissions.AdminTemplatesPublish, false)]
    [InlineData(false, new[] { Permissions.AdminTemplatesPublish }, Permissions.AdminTemplatesPublish, false)]
    [InlineData(true, new string[0], null, true)]
    [InlineData(false, new string[0], null, false)]
    public async Task The_policy_needs_the_internal_claim_and_for_a_tier_that_tier(bool isInternal, string[] permissions, string? tier, bool expected)
    {
        var claims = permissions.Select(p => new Claim(SessionTokenIssuer.PermissionsClaim, p)).ToList();
        if (isInternal)
        {
            claims.Add(new Claim(SessionTokenIssuer.InternalClaim, "true"));
        }

        var requirement = new InternalTierRequirement(tier);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null);

        await new InternalTierHandler().HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("TRUE ")]
    [InlineData("1")]
    public async Task Only_the_exact_value_true_counts_as_internal(string value)
    {
        var requirement = new InternalTierRequirement(null);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionTokenIssuer.InternalClaim, value)], "test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new InternalTierHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
