namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// A push of a View Template. It keeps a snapshot of the template as it was pushed, so editing or unpublishing the template later never changes
/// what the push delivers. Revoking it stops future deliveries and leaves delivered copies alone.
/// </summary>
public sealed class TemplateAssignment
{
    public int Id { get; set; }

    public int ViewTemplateId { get; set; }

    /// <summary>The audience: <c>All</c> or <c>Provider</c>.</summary>
    public string ScopeKind { get; set; } = "";

    /// <summary>The Provider (portal company id) for a <c>Provider</c> assignment, otherwise null.</summary>
    public int? ProviderId { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string Layout { get; set; } = "{}";

    public int LayoutSchemaVersion { get; set; } = 1;

    public DateTimeOffset? RevokedAtTimestamp { get; set; }

    public long CreatedByPortalUserId { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
