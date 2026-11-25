namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>Per-Provider role settings, set by EMS staff only. A Provider with no row uses the defaults: Viewer, and restricted grants off.</summary>
public sealed class ProviderSetting
{
    public int Id { get; set; }

    public int ProviderId { get; set; }

    public string DefaultRoleKey { get; set; } = "viewer";

    public bool AllowAdminRestrictedGrants { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
