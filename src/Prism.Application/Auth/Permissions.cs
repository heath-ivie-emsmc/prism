namespace Prism.Application.Auth;

/// <summary>
/// The one list of Prism permissions and roles. Sign-in reads it to turn portal role names into permissions, and the
/// Admin Portal setup utility publishes the same list to the portal, so the two cannot drift apart.
/// </summary>
public static class Permissions
{
    public const string App = "prism.app";
    public const string DashboardView = "prism.dashboard.view";
    public const string PhiView = "prism.phi.view";
    public const string SettingsManage = "prism.settings.manage";
    public const string ViewsManage = "prism.views.manage";

    private const string Group = "Prism";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(App, "Prism (sign in)", Group),
        new(DashboardView, "Dashboard: view", Group),
        new(PhiView, "Patient-level data: view (reserved, not used yet)", Group),
        new(SettingsManage, "Provider settings: manage", Group),
        new(ViewsManage, "Saved views: create, edit, delete and share", Group),
    ];

    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new("ProviderAnalyst", [App, DashboardView, ViewsManage]),
        new("ProviderAdmin", [App, DashboardView, SettingsManage, ViewsManage]),
        new("PlatformSupport", [App, DashboardView]),
        new("SuperUser", [App, DashboardView, PhiView, SettingsManage, ViewsManage]),
    ];

    /// <summary>
    /// Permissions for the roles the portal reports. A reported role matches a Prism role only by its exact name, or as
    /// <c>&lt;prefix&gt;__&lt;name&gt;</c> where the prefix is one of <paramref name="acceptedPrefixes"/> (the portal can prefix
    /// application roles). Case is ignored. Anything else, including the same role name under another application's prefix, grants nothing.
    /// </summary>
    public static IReadOnlyList<string> ForRoles(IReadOnlyCollection<string> roles, IReadOnlyCollection<string>? acceptedPrefixes = null)
    {
        acceptedPrefixes ??= [];
        HashSet<string> granted = Roles
            .Where(definition => roles.Any(role => Matches(definition.Name, role, acceptedPrefixes)))
            .SelectMany(definition => definition.Permissions)
            .ToHashSet(StringComparer.Ordinal);

        return All.Where(permission => granted.Contains(permission.Id)).Select(permission => permission.Id).ToList();
    }

    private static bool Matches(string definedRole, string reportedRole, IReadOnlyCollection<string> acceptedPrefixes)
    {
        string name = reportedRole.Trim();
        return string.Equals(definedRole, name, StringComparison.OrdinalIgnoreCase)
            || acceptedPrefixes.Any(prefix =>
                !string.IsNullOrWhiteSpace(prefix)
                && string.Equals($"{prefix.Trim()}__{definedRole}", name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A permission as registered in the Admin Portal. <paramref name="Parent"/> is null for a top-level entry.</summary>
public sealed record PermissionDefinition(string Id, string Name, string Group, string? Parent = null);

/// <summary>A core role as registered in the Admin Portal, with the permission ids it carries.</summary>
public sealed record RoleDefinition(string Name, IReadOnlyList<string> Permissions);
