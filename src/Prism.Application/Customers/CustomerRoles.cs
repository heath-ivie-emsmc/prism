using Prism.Application.Auth;

namespace Prism.Application.Customers;

/// <summary>A Customer Role: a named set of permissions a customer user holds in one Provider. <paramref name="Restricted"/> roles carry claim or PHI access.</summary>
public sealed record CustomerRole(string Key, string Name, string Description, IReadOnlyList<string> Permissions, bool Restricted)
{
    public bool CanManageUsers => Permissions.Contains(Prism.Application.Auth.Permissions.UsersManage);
}

/// <summary>
/// The fixed catalog of Customer Roles. A role cannot be created or changed at runtime, so the authorization and PHI tests can enumerate every one.
/// The right to sign in (<c>prism.app</c>) is not part of a role: it comes from the portal.
/// </summary>
public static class CustomerRoles
{
    public const string Viewer = "viewer";
    public const string Analyst = "analyst";
    public const string ClaimsReviewer = "claims_reviewer";
    public const string PhiReviewer = "phi_reviewer";
    public const string ProviderAdmin = "provider_admin";
    public const string ClaimsAdmin = "claims_admin";

    public const int MaxKeyLength = 30;

    public static IReadOnlyList<CustomerRole> All { get; } =
    [
        new(Viewer, "Viewer", "See the dashboards and metrics.", [Permissions.DashboardView], false),
        new(Analyst, "Analyst", "Viewer, and create and share saved views.", [Permissions.DashboardView, Permissions.ViewsManage], false),
        new(ClaimsReviewer, "Claims reviewer", "Analyst, and review the claims behind a count.", [Permissions.DashboardView, Permissions.ViewsManage, Permissions.ClaimsView], true),
        new(PhiReviewer, "PHI reviewer", "Claims reviewer, and see patient details.", [Permissions.DashboardView, Permissions.ViewsManage, Permissions.ClaimsView, Permissions.PhiView], true),
        new(ProviderAdmin, "Provider admin", "Analyst, and manage settings and the roles of the Provider's users.", [Permissions.DashboardView, Permissions.ViewsManage, Permissions.SettingsManage, Permissions.UsersManage], false),
        new(ClaimsAdmin, "Provider admin with claims", "Provider admin, and review the claims behind a count.", [Permissions.DashboardView, Permissions.ViewsManage, Permissions.SettingsManage, Permissions.UsersManage, Permissions.ClaimsView], true),
    ];

    public static CustomerRole? Find(string? key) => All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    public static IReadOnlySet<string> PermissionsOf(string? key) => (Find(key)?.Permissions ?? []).ToHashSet(StringComparer.Ordinal);
}
