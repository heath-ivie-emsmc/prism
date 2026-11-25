using Prism.Application.Admin;

namespace Prism.Application.Personas;

public sealed record PersonaContent(string Name, int ViewTemplateId, int SortOrder);

public sealed record PersonaDetail(int Id, string Name, int ViewTemplateId, string? ViewTemplateName, int SortOrder, bool IsPublished);

public enum PersonaWriteStatus
{
    Saved,
    NotFound,
    NameTaken,
    LimitReached,

    /// <summary>The View Template does not exist.</summary>
    TemplateMissing,

    /// <summary>Publishing needs a published View Template.</summary>
    TemplateNotPublished,

    /// <summary>Deleting is refused while users or question weights point at the Persona.</summary>
    InUse,
}

public sealed record PersonaWriteResult(PersonaWriteStatus Status, PersonaDetail? Persona = null);

/// <summary>Personas, for Internal Users. The caller has already been authorized for the tier.</summary>
public interface IPersonaStore
{
    Task<IReadOnlyList<PersonaDetail>> ListAsync(CancellationToken cancellationToken);

    Task<PersonaDetail?> GetAsync(int personaId, CancellationToken cancellationToken);

    Task<PersonaWriteResult> CreateAsync(AdminActor actor, PersonaContent content, CancellationToken cancellationToken);

    Task<PersonaWriteResult> UpdateAsync(AdminActor actor, int personaId, PersonaContent content, CancellationToken cancellationToken);

    Task<PersonaWriteResult> SetPublishedAsync(AdminActor actor, int personaId, bool published, CancellationToken cancellationToken);

    Task<PersonaWriteResult> DeleteAsync(AdminActor actor, int personaId, CancellationToken cancellationToken);
}
