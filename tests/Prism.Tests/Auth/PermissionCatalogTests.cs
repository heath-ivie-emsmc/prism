using Prism.Application.Auth;

namespace Prism.Tests.Auth;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void An_analyst_can_sign_in_view_the_dashboard_and_manage_saved_views()
    {
        Assert.Equal([Permissions.App, Permissions.DashboardView, Permissions.ViewsManage], Permissions.ForRoles(["ProviderAnalyst"]));
    }

    [Fact]
    public void A_provider_admin_can_also_manage_settings()
    {
        Assert.Equal(
            [Permissions.App, Permissions.DashboardView, Permissions.SettingsManage, Permissions.ViewsManage],
            Permissions.ForRoles(["ProviderAdmin"]));
    }

    [Fact]
    public void A_super_user_gets_every_permission_in_catalog_order()
    {
        Assert.Equal(Permissions.All.Select(p => p.Id), Permissions.ForRoles(["SuperUser"]));
    }

    [Fact]
    public void Several_roles_give_the_union_without_duplicates()
    {
        IReadOnlyList<string> granted = Permissions.ForRoles(["ProviderAnalyst", "PlatformSupport", "ProviderAdmin"]);

        Assert.Equal([Permissions.App, Permissions.DashboardView, Permissions.SettingsManage, Permissions.ViewsManage], granted);
    }

    [Theory]
    [InlineData("provideranalyst")]
    [InlineData("PROVIDERANALYST")]
    [InlineData("  ProviderAnalyst  ")]
    public void Role_names_match_ignoring_case_and_surrounding_spaces(string reported)
    {
        Assert.Contains(Permissions.App, Permissions.ForRoles([reported]));
    }

    [Theory]
    [InlineData("prism-localhost__ProviderAnalyst")]
    [InlineData("PRISM-LOCALHOST__provideranalyst")]
    [InlineData("shared__ProviderAnalyst")]
    public void A_role_with_an_accepted_prefix_matches(string reported)
    {
        Assert.Contains(Permissions.App, Permissions.ForRoles([reported], ["prism-localhost", "shared"]));
    }

    [Theory]
    [InlineData("m6__SuperUser")]
    [InlineData("client__ProviderAdmin")]
    [InlineData("Prism__ProviderAnalyst")]
    [InlineData("a__prism-localhost__ProviderAnalyst")]
    [InlineData("__ProviderAnalyst")]
    public void A_role_under_another_applications_prefix_never_matches(string reported)
    {
        Assert.Empty(Permissions.ForRoles([reported], ["prism-localhost"]));
        Assert.Empty(Permissions.ForRoles([reported]));
    }

    [Fact]
    public void A_blank_accepted_prefix_does_not_turn_into_a_wildcard()
    {
        Assert.Empty(Permissions.ForRoles(["__SuperUser", "x__SuperUser"], ["", "  "]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Analyst")]
    [InlineData("xProviderAnalyst")]
    [InlineData("ProviderAnalystx")]
    [InlineData("ProviderAnalyst__")]
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
    public void Every_defined_permission_is_held_by_at_least_one_role()
    {
        HashSet<string> held = Permissions.Roles.SelectMany(r => r.Permissions).ToHashSet();

        Assert.All(Permissions.All, p => Assert.Contains(p.Id, held));
    }
}
