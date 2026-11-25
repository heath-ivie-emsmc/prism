namespace Prism.Application.Auth;

/// <summary>
/// The one list of Prism permissions and roles. Sign-in reads it to turn portal role names into permissions, and the
/// Admin Portal setup utility publishes the same list to the portal, so the two cannot drift apart.
/// </summary>
public static class Permissions
{
    public const string App = "prism.app";
    public const string DashboardView = "prism.dashboard.view";
    public const string ClaimsView = "prism.claims.view";
    public const string PhiView = "prism.phi.view";
    public const string SettingsManage = "prism.settings.manage";
    public const string ViewsManage = "prism.views.manage";
    public const string UsersManage = "prism.users.manage";

    /// <summary>Marks an employee. The portal reports no group names, so a portal role carries this and a portal admin attaches the role to the internal group. It grants nothing by itself.</summary>
    public const string InternalUser = "prism.internal";

    // Internal admin tiers. They only count for a user who is also in the internal portal group (see InternalAccess).
    public const string AdminTemplatesAuthor = "prism.admin.templates.author";
    public const string AdminTemplatesPublish = "prism.admin.templates.publish";
    public const string AdminCustomersView = "prism.admin.customers.view";
    public const string AdminManage = "prism.admin.manage";

    private const string Group = "Prism";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(App, "Prism (sign in)", Group),
        new(DashboardView, "Dashboard: view", Group),
        new(ClaimsView, "Claim-level data: view and export", Group),
        new(PhiView, "Patient-level data: view (reserved, not used yet)", Group),
        new(SettingsManage, "Provider settings: manage", Group),
        new(ViewsManage, "Saved views: create, edit, delete and share", Group),
        new(UsersManage, "Users: manage the roles of a Provider's users", Group),
        new(InternalUser, "Internal: marks an employee (no customer Providers, may enter the admin area with a tier)", Group),
        new(AdminTemplatesAuthor, "Admin: create and edit View Templates, Personas and the questionnaire", Group),
        new(AdminTemplatesPublish, "Admin: push View Templates to Providers, Personas or everyone", Group),
        new(AdminCustomersView, "Admin: view a customer's dashboards as one of their users (read-only)", Group),
        new(AdminManage, "Admin: manage admin settings and read the admin audit log", Group),
    ];

    /// <summary>The permissions that mark an admin tier. A customer role never carries one.</summary>
    public static IReadOnlyList<string> AdminTiers { get; } = [AdminTemplatesAuthor, AdminTemplatesPublish, AdminCustomersView, AdminManage];

    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        // Login only: the portal decides who may sign in, and what the user may do in each Provider is a Customer Role kept in Prism (ADR 0010).
        new("PrismUser", [App]),
        new("InternalUser", [App, InternalUser]),
        new("InternalTemplateAuthor", [App, AdminTemplatesAuthor]),
        new("InternalTemplatePublisher", [App, AdminTemplatesPublish]),
        new("InternalCustomerDataViewer", [App, DashboardView, AdminCustomersView]),
        new("InternalAdminManager", [App, AdminManage]),
    ];

    /// <summary>The permissions published to the portal: those a portal role carries. Customer permissions live in Prism (Customer Roles) and are not published.</summary>
    public static IReadOnlyList<PermissionDefinition> Published { get; } = [.. All.Where(p => Roles.SelectMany(r => r.Permissions).Contains(p.Id))];

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
