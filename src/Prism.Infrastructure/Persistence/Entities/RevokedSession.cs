namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>A session that was ended before its expiry. Rows older than <see cref="SessionExpiresAtTimestamp"/> are dead weight and get purged.</summary>
public sealed class RevokedSession
{
    public int Id { get; set; }

    /// <summary>The session token's <c>jti</c> claim.</summary>
    public string SessionId { get; set; } = "";

    public string UserName { get; set; } = "";

    /// <summary>When the session token expires on its own. After this the revocation row has no effect.</summary>
    public DateTimeOffset SessionExpiresAtTimestamp { get; set; }

    /// <summary>Set by the database trigger. This is also when the session was revoked.</summary>
    public DateTimeOffset CreatedAtTimestamp { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
