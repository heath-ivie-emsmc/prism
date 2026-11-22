namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// An emailed, time-limited, single-use invitation to open a Saved View. Only a hash of the token is stored, so a database reader cannot redeem one.
/// </summary>
public sealed class SavedViewInvitation
{
    public int Id { get; set; }

    public int SavedViewId { get; set; }

    /// <summary>The Provider of the view when the invitation was made. Checked again at redemption.</summary>
    public int ProviderId { get; set; }

    /// <summary>The address the invitation was sent to, trimmed and lower-cased.</summary>
    public string InvitedEmail { get; set; } = "";

    /// <summary>SHA-256 of the token, as 64 hex characters.</summary>
    public string TokenHash { get; set; } = "";

    public DateTimeOffset ExpiresAtTimestamp { get; set; }

    public DateTimeOffset? RedeemedAtTimestamp { get; set; }

    public long? RedeemedByPortalUserId { get; set; }

    public DateTimeOffset? RevokedAtTimestamp { get; set; }

    public long CreatedByPortalUserId { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset CreatedAtTimestamp { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
