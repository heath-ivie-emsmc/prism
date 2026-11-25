using Prism.Application.Auth;

namespace Prism.Tests.Auth;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void The_login_only_role_grants_sign_in_and_nothing_else()
    {
        Assert.Equal([Permissions.App], Permissions.ForRoles(["PrismUser"]));
    }

    [Theory]
    [InlineData("InternalTemplateAuthor", Permissions.AdminTemplatesAuthor)]
    [InlineData("InternalTemplatePublisher", Permissions.AdminTemplatesPublish)]
    [InlineData("InternalAdminManager", Permissions.AdminManage)]
    public void Each_admin_tier_role_carries_only_its_own_tier_and_sign_in(string role, string tier)
    {
        Assert.Equal([Permissions.App, tier], Permissions.ForRoles([role]));
    }

    [Fact]
    public void The_customer_data_viewer_role_also_carries_the_dashboard_permission_but_never_claim_level_data()
    {
        IReadOnlyList<string> granted = Permissions.ForRoles(["InternalCustomerDataViewer"]);

        Assert.Equal([Permissions.App, Permissions.DashboardView, Permissions.AdminCustomersView], granted);
        Assert.DoesNotContain(Permissions.ClaimsView, granted);
        Assert.DoesNotContain(Permissions.PhiView, granted);
    }

    [Fact]
    public void The_login_only_role_carries_no_admin_tier_and_no_internal_marker()
    {
        IReadOnlyList<string> granted = Permissions.ForRoles(["PrismUser"]);

        Assert.Empty(granted.Intersect(Permissions.AdminTiers));
        Assert.DoesNotContain(Permissions.InternalUser, granted);
    }

    [Fact]
    public void The_internal_marker_role_carries_the_marker_and_no_tier()
    {
        Assert.Equal([Permissions.App, Permissions.InternalUser], Permissions.ForRoles(["InternalUser"]));
    }

    [Fact]
    public void The_roles_the_portal_used_to_hold_for_customers_no_longer_exist()
    {
        Assert.Empty(Permissions.ForRoles(["ProviderAnalyst", "ProviderAdmin", "PlatformSupport", "SuperUser"]));
    }

    [Fact]
    public void Several_roles_give_the_union_without_duplicates()
    {
        IReadOnlyList<string> granted = Permissions.ForRoles(["PrismUser", "InternalCustomerDataViewer", "InternalAdminManager"]);

        Assert.Equal([Permissions.App, Permissions.DashboardView, Permissions.AdminCustomersView, Permissions.AdminManage], granted);
    }

    [Theory]
    [InlineData("prismuser")]
    [InlineData("PRISMUSER")]
    [InlineData("  PrismUser  ")]
    public void Role_names_match_ignoring_case_and_surrounding_spaces(string reported)
    {
        Assert.Contains(Permissions.App, Permissions.ForRoles([reported]));
    }

    [Theory]
    [InlineData("prism-localhost__PrismUser")]
    [InlineData("PRISM-LOCALHOST__prismuser")]
    [InlineData("shared__PrismUser")]
    public void A_role_with_an_accepted_prefix_matches(string reported)
    {
        Assert.Contains(Permissions.App, Permissions.ForRoles([reported], ["prism-localhost", "shared"]));
    }

    [Theory]
    [InlineData("m6__PrismUser")]
    [InlineData("client__PrismUser")]
    [InlineData("Prism__PrismUser")]
    [InlineData("a__prism-localhost__PrismUser")]
    [InlineData("__PrismUser")]
    public void A_role_under_another_applications_prefix_never_matches(string reported)
    {
        Assert.Empty(Permissions.ForRoles([reported], ["prism-localhost"]));
        Assert.Empty(Permissions.ForRoles([reported]));
    }

    [Fact]
    public void A_blank_accepted_prefix_does_not_turn_into_a_wildcard()
    {
        Assert.Empty(Permissions.ForRoles(["__PrismUser", "x__PrismUser"], ["", "  "]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("User")]
    [InlineData("xPrismUser")]
    [InlineData("PrismUserx")]
    [InlineData("PrismUser__")]
    [InlineData("SomeOtherAppRole")]
    public void Unknown_roles_grant_nothing(string reported)
    {
        Assert.Empty(Permissions.ForRoles([reported]));
    }

    [Fact]
    public void No_roles_grant_nothing()
    {
        Assert.Empty(Permissions.ForRoles([]));
    }

    [Fact]
    public void Every_role_refers_only_to_defined_permissions_and_includes_sign_in()
    {
        HashSet<string> defined = Permissions.All.Select(p => p.Id).ToHashSet();

        foreach (RoleDefinition role in Permissions.Roles)
        {
            Assert.All(role.Permissions, id => Assert.Contains(id, defined));
            Assert.Contains(Permissions.App, role.Permissions);
        }
    }

    [Fact]
    public void Permission_ids_and_role_names_are_unique_and_prism_scoped()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Id).Distinct().Count());
        Assert.Equal(Permissions.Roles.Count, Permissions.Roles.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Permissions.All, p => Assert.StartsWith("prism.", p.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_published_permission_is_held_by_at_least_one_role()
    {
        HashSet<string> held = Permissions.Roles.SelectMany(r => r.Permissions).ToHashSet();

        Assert.All(Permissions.Published, p => Assert.Contains(p.Id, held));
    }
}
