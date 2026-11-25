using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Application.Personas;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Personas;

/// <summary>Personas in Prism's own database. Each change and its audit record are saved in one transaction.</summary>
public sealed class PersonaStore(PrismDbContext db) : IPersonaStore
{
    public async Task<IReadOnlyList<PersonaDetail>> ListAsync(CancellationToken cancellationToken)
    {
        List<Persona> personas = await db.Personas.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(PersonaLimits.MaxPersonas)
            .ToListAsync(cancellationToken);
        Dictionary<int, string> templateNames = await TemplateNamesAsync(personas.Select(p => p.ViewTemplateId), cancellationToken);
        return [.. personas.Select(p => Describe(p, templateNames))];
    }

    public async Task<PersonaDetail?> GetAsync(int personaId, CancellationToken cancellationToken)
    {
        Persona? persona = await db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        return persona is null ? null : Describe(persona, await TemplateNamesAsync([persona.ViewTemplateId], cancellationToken));
    }

    public async Task<PersonaWriteResult> CreateAsync(AdminActor actor, PersonaContent content, CancellationToken cancellationToken)
    {
        if (await db.Personas.CountAsync(cancellationToken) >= PersonaLimits.MaxPersonas)
        {
            return new PersonaWriteResult(PersonaWriteStatus.LimitReached);
        }

        if (!await db.ViewTemplates.AnyAsync(t => t.Id == content.ViewTemplateId, cancellationToken))
        {
            return new PersonaWriteResult(PersonaWriteStatus.TemplateMissing);
        }

        var persona = new Persona { CreatedByUserName = actor.UserName, UpdatedByUserName = actor.UserName };
        Apply(persona, content);
        db.Personas.Add(persona);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.PersonaCreated, AdminAuditObjects.Persona, persona.Id));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new PersonaWriteResult(PersonaWriteStatus.NameTaken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PersonaWriteResult(PersonaWriteStatus.Saved, await GetAsync(persona.Id, cancellationToken));
    }

    public async Task<PersonaWriteResult> UpdateAsync(AdminActor actor, int personaId, PersonaContent content, CancellationToken cancellationToken)
    {
        Persona? persona = await db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        if (persona is null)
        {
            return new PersonaWriteResult(PersonaWriteStatus.NotFound);
        }

        ViewTemplate? template = await db.ViewTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == content.ViewTemplateId, cancellationToken);
        if (template is null)
        {
            return new PersonaWriteResult(PersonaWriteStatus.TemplateMissing);
        }

        // A published Persona must keep pointing at a published template, or users would be left with nothing to follow.
        if (persona.IsPublished && !template.IsPublished)
        {
            return new PersonaWriteResult(PersonaWriteStatus.TemplateNotPublished);
        }

        Apply(persona, content);
        persona.UpdatedByUserName = actor.UserName;
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.PersonaUpdated, AdminAuditObjects.Persona, persona.Id));
        return await SaveAsync(persona.Id, cancellationToken);
    }

    public async Task<PersonaWriteResult> SetPublishedAsync(AdminActor actor, int personaId, bool published, CancellationToken cancellationToken)
    {
        Persona? persona = await db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        if (persona is null)
        {
            return new PersonaWriteResult(PersonaWriteStatus.NotFound);
        }

        if (published && !await db.ViewTemplates.AnyAsync(t => t.Id == persona.ViewTemplateId && t.IsPublished, cancellationToken))
        {
            return new PersonaWriteResult(PersonaWriteStatus.TemplateNotPublished);
        }

        persona.IsPublished = published;
        persona.UpdatedByUserName = actor.UserName;
        string action = published ? AdminAuditActions.PersonaPublished : AdminAuditActions.PersonaUnpublished;
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(action, AdminAuditObjects.Persona, persona.Id));
        return await SaveAsync(persona.Id, cancellationToken);
    }

    public async Task<PersonaWriteResult> DeleteAsync(AdminActor actor, int personaId, CancellationToken cancellationToken)
    {
        if (!await db.Personas.AnyAsync(p => p.Id == personaId, cancellationToken))
        {
            return new PersonaWriteResult(PersonaWriteStatus.NotFound);
        }

        if (await db.UserProfiles.AnyAsync(u => u.PersonaId == personaId, cancellationToken)
            || await db.OnboardingOptionWeights.AnyAsync(w => w.PersonaId == personaId, cancellationToken))
        {
            return new PersonaWriteResult(PersonaWriteStatus.InUse);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int removed = await db.Personas.Where(p => p.Id == personaId).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return new PersonaWriteResult(PersonaWriteStatus.NotFound);
        }

        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.PersonaDeleted, AdminAuditObjects.Persona, personaId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PersonaWriteResult(PersonaWriteStatus.Saved);
    }

    private async Task<PersonaWriteResult> SaveAsync(int personaId, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new PersonaWriteResult(PersonaWriteStatus.NameTaken);
        }

        db.ChangeTracker.Clear();
        return new PersonaWriteResult(PersonaWriteStatus.Saved, await GetAsync(personaId, cancellationToken));
    }

    private async Task<Dictionary<int, string>> TemplateNamesAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        int[] wanted = [.. ids.Distinct()];
        return await db.ViewTemplates.AsNoTracking().Where(t => wanted.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
    }

    private static PersonaDetail Describe(Persona p, Dictionary<int, string> templateNames) =>
        new(p.Id, p.Name, p.ViewTemplateId, templateNames.GetValueOrDefault(p.ViewTemplateId), p.SortOrder, p.IsPublished);

    private static void Apply(Persona persona, PersonaContent content)
    {
        persona.Name = content.Name;
        persona.ViewTemplateId = content.ViewTemplateId;
        persona.SortOrder = content.SortOrder;
    }
}
