namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>That one user received one Assignment in one Provider. It makes delivery happen once, even if the user deletes the copy.</summary>
public sealed class TemplateDelivery
{
    public int Id { get; set; }

    public int TemplateAssignmentId { get; set; }

    public int ProviderId { get; set; }

    public long PortalUserId { get; set; }

    /// <summary>The clone. Null once the user has deleted it; the application clears it, as the foreign key restricts.</summary>
    public int? SavedViewId { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
