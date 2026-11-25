namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>A layout authored by Internal Users. Metric keys, presentations and positions only: no owner, no Provider, no figures.</summary>
public sealed class ViewTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>The layout as JSON, validated against the Metric Catalog before it is stored.</summary>
    public string Layout { get; set; } = "{}";

    public int LayoutSchemaVersion { get; set; } = 1;

    /// <summary>Only a published template can be pushed.</summary>
    public bool IsPublished { get; set; }

    /// <summary>PostgreSQL's <c>xmin</c>. A save made from a stale copy is refused.</summary>
    public uint RowVersion { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
