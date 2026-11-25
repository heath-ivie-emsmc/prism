namespace Prism.Application.Auth;

/// <summary>
/// Who counts as an Internal User. Membership of the one portal group named here is the gate to the admin area. It grants nothing
/// by itself: what an Internal User may do depends on a separate tier permission.
/// </summary>
public sealed class InternalAccessOptions
{
    public const string SectionName = "Internal";

    public const int MaxGroupNameLength = 200;

    /// <summary>Optional: a portal user group whose members are Internal Users, used only if the portal reports group names (it does not today). The marker role is the normal signal.</summary>
    public string GroupName { get; set; } = "";
}

/// <summary>The two-part rule for admin access: the internal group, then a tier permission. Both are required.</summary>
public static class InternalAccess
{
    /// <summary>True when the user belongs to the configured group. A blank configured name matches nobody (fails closed).</summary>
    public static bool IsInternal(IEnumerable<string> userGroups, string configuredGroupName)
    {
        string wanted = configuredGroupName.Trim();
        return wanted.Length > 0
            && userGroups.Any(group => string.Equals(group.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when the user holds the <see cref="Permissions.InternalUser"/> marker role, or belongs to the configured group when the portal reports one.
    /// The portal's APIs return no group names, so in practice the marker role (attached to the group in the portal) is the signal.
    /// </summary>
    public static bool IsInternal(IEnumerable<string> userGroups, string configuredGroupName, IEnumerable<string> permissions) =>
        permissions.Contains(Permissions.InternalUser) || IsInternal(userGroups, configuredGroupName);

    /// <summary>The permissions a user keeps: an admin tier survives only for an Internal User, so a tier without the group grants nothing.</summary>
    public static IReadOnlyList<string> Filter(IReadOnlyList<string> permissions, bool isInternal) =>
        isInternal ? permissions : permissions.Where(permission => !Permissions.AdminTiers.Contains(permission)).ToList();
}
