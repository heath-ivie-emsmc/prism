namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// A dashboard layout a user arranged and saved. It holds Metric keys and positions only: no figures, no Labels, no filter values. The owner and Provider are
/// set by server code from the session and the route, never from a request body.
/// </summary>
public sealed class SavedView
{
    public int Id { get; set; }

    /// <summary>The Provider (portal company id) the view belongs to. Every query is scoped by it.</summary>
    public int ProviderId { get; set; }

    /// <summary>The portal user id of the owner, from the session.</summary>
    public long OwnerPortalUserId { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>The layout as JSON, validated against the Metric Catalog before it is stored.</summary>
    public string Layout { get; set; } = "{}";

    /// <summary>The version of the layout JSON shape, so old layouts can be upgraded on read.</summary>
    public int LayoutSchemaVersion { get; set; } = 1;

    /// <summary>PostgreSQL's <c>xmin</c>. Changes on every update, so a save made from a stale copy is refused.</summary>
    public uint RowVersion { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset CreatedAtTimestamp { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
