namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// One read of claim-level data. It records who, what kind, for which Provider, period and check, and how many rows. It never holds a claim
/// reference, a name, an amount or a filter value. <see cref="CreatedAtTimestamp"/> is when the read happened.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>The Provider (portal company id), set from the access check and never from a request.</summary>
    public int ProviderId { get; set; }

    public long PortalUserId { get; set; }

    public string Action { get; set; } = "";

    public string CheckKey { get; set; } = "";

    /// <summary>The reporting period as yyyyMM.</summary>
    public string Period { get; set; } = "";

    public int RowCount { get; set; }

    /// <summary>The aircraft-type key the read was narrowed to, or null for the whole period.</summary>
    public string? GroupKey { get; set; }

    /// <summary>Set by the database trigger. This is when the read happened.</summary>
    public DateTimeOffset CreatedAtTimestamp { get; set; }

    /// <summary>Set by the database trigger.</summary>
    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
