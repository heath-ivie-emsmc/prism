namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// One action by an Internal User in the admin area. It records who, what, which kind of object and which Provider, and how many things
/// it touched. It never holds free text, a layout, a name or any customer data. <see cref="CreatedAtTimestamp"/> is when it happened.
/// </summary>
public sealed class AdminAuditEntry
{
    public long Id { get; set; }

    /// <summary>The Internal User, from the session.</summary>
    public long ActorPortalUserId { get; set; }

    /// <summary>One of the admin audit action codes.</summary>
    public string Action { get; set; } = "";

    /// <summary>The kind of object touched, such as <c>view_template</c> or <c>template_assignment</c>.</summary>
    public string ObjectType { get; set; } = "";

    public int? ObjectId { get; set; }

    /// <summary>The Provider (portal company id) the action targeted, when it targeted one.</summary>
    public int? ProviderId { get; set; }

    /// <summary>How many things the action touched, such as the widgets in a pushed template.</summary>
    public int? AffectedCount { get; set; }

    /// <summary>The user a customer role change was about. A portal user id, never a name.</summary>
    public long? TargetPortalUserId { get; set; }

    /// <summary>A short code for what changed, such as the old and new role keys. Never free text.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
