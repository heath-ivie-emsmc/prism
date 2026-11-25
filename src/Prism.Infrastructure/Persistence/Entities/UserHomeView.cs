namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>A user's own copy of the Persona's home page in one Provider. While it exists it replaces the live reference.</summary>
public sealed class UserHomeView
{
    public int Id { get; set; }

    public long PortalUserId { get; set; }

    public int ProviderId { get; set; }

    public int SavedViewId { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
