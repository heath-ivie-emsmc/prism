namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>A user who may open a Saved View. Created when an invitation is redeemed, removed when the owner revokes access. Read-only for the recipient.</summary>
public sealed class SavedViewShare
{
    public int Id { get; set; }

    public int SavedViewId { get; set; }

    /// <summary>The portal user id of the recipient, from the session of the person who redeemed the invitation.</summary>
    public long SharedWithPortalUserId { get; set; }

    /// <summary>The invitation that produced this share, if any.</summary>
    public int? SavedViewInvitationId { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset CreatedAtTimestamp { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
