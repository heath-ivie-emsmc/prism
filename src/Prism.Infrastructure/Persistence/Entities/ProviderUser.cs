namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>A customer user in one Provider and the Customer Role they hold there. Recorded when they sign in.</summary>
public sealed class ProviderUser
{
    public int Id { get; set; }

    public int ProviderId { get; set; }

    public long PortalUserId { get; set; }

    /// <summary>The portal's display name, shown to admins. Bound as text only.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>A key in the Customer Role catalog.</summary>
    public string RoleKey { get; set; } = "";

    public DateTimeOffset LastSignInTimestamp { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
