namespace Prism.Application.Customers;

public static class CustomerAccessLimits
{
    public const int PageSize = 50;
    public const int MaxPage = 1000;
    public const int MaxRequestBodyBytes = 4 * 1024;
    public const int MaxProvidersRecorded = 100;
    public const int MaxDisplayNameLength = 200;
}

/// <summary>
/// What a customer user may do, per Provider, from their Customer Role. Used on every customer request. A user with no record, or a role that cannot be
/// read, has no permission: callers treat both as a denial.
/// </summary>
public interface ICustomerAccess
{
    /// <summary>The permissions the user holds in the Provider. Empty when they have no role there. Throws when the store cannot be read.</summary>
    Task<IReadOnlySet<string>> PermissionsAsync(int providerId, long portalUserId, CancellationToken cancellationToken);

    /// <summary>The permissions the user holds in at least one Provider, for routes that are not under a Provider.</summary>
    Task<IReadOnlySet<string>> PermissionsInAnyProviderAsync(long portalUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the user in each Provider (a new one gets the Provider's default role), notes the sign-in,
    /// and returns what they may do in each. Never called for an Internal User. The Providers are the ones the portal assigned.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> RecordAsync(
        long portalUserId,
        string displayName,
        IReadOnlyList<int> providerIds,
        CancellationToken cancellationToken);
}

/// <summary>Who is changing a role. Built by server code from the session and the route's policy, never from a request body.</summary>
public sealed record RoleActor(long PortalUserId, string UserName, bool IsStaff);

public sealed record ProviderUserView(long PortalUserId, string DisplayName, string RoleKey, DateTimeOffset LastSignIn);

public sealed record ProviderUserPage(IReadOnlyList<ProviderUserView> Users, bool HasMore);

public sealed record ProviderRoleSettings(string DefaultRoleKey, bool AllowAdminRestrictedGrants);

public enum RoleChangeStatus
{
    Changed,
    NotFound,
    UnknownRole,

    /// <summary>A Provider admin changing their own role.</summary>
    OwnRole,

    /// <summary>A Provider admin assigning a restricted role while the Provider does not allow it.</summary>
    RestrictedRole,

    /// <summary>A Provider admin removing the last user who can manage users.</summary>
    LastAdmin,

    /// <summary>The role is not allowed for this change (a default role must be ordinary).</summary>
    NotAllowed,
}

public sealed record RoleChangeResult(RoleChangeStatus Status, ProviderUserView? User = null);

/// <summary>Listing and changing the roles of a Provider's users, for a Provider admin (own Provider) or EMS staff (a named Provider). The caller has been authorized.</summary>
public interface ICustomerUserAdmin
{
    Task<ProviderUserPage> ListAsync(int providerId, int page, CancellationToken cancellationToken);

    Task<RoleChangeResult> SetRoleAsync(RoleActor actor, int providerId, long targetPortalUserId, string roleKey, CancellationToken cancellationToken);

    Task<ProviderRoleSettings> GetSettingsAsync(int providerId, CancellationToken cancellationToken);

    /// <summary>Staff only. The default role must be an ordinary one that cannot manage users.</summary>
    Task<RoleChangeResult> SetSettingsAsync(RoleActor actor, int providerId, ProviderRoleSettings settings, CancellationToken cancellationToken);

    /// <summary>Whether a Provider admin may assign restricted roles in this Provider.</summary>
    Task<bool> AllowsAdminRestrictedGrantsAsync(int providerId, CancellationToken cancellationToken);
}
