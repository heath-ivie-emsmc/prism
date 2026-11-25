namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>The kind of user someone is. It selects a home page by pointing at a View Template. Only a published Persona is ever used for a user.</summary>
public sealed class Persona
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int ViewTemplateId { get; set; }

    /// <summary>The lower value wins a tie between Personas.</summary>
    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
