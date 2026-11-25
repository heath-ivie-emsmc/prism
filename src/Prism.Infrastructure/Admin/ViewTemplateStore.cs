using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Admin;

/// <summary>View Templates in Prism's own database. Each change and its audit record are saved in one transaction.</summary>
public sealed class ViewTemplateStore(PrismDbContext db) : IViewTemplateStore
{
    public async Task<IReadOnlyList<ViewTemplateSummary>> ListAsync(CancellationToken cancellationToken)
    {
        List<ViewTemplate> templates = await db.ViewTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .Take(ViewTemplateLimits.MaxTemplates)
            .ToListAsync(cancellationToken);
        return [.. templates.Select(t => new ViewTemplateSummary(t.Id, t.Name, t.Description, t.IsPublished, AdminStoreSupport.WidgetCount(t.Layout), t.UpdatedAtTimestamp))];
    }

    public async Task<ViewTemplateDetail?> GetAsync(int templateId, CancellationToken cancellationToken)
    {
        ViewTemplate? template = await db.ViewTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        return template is null ? null : Describe(template);
    }

    public async Task<ViewTemplateWriteResult> CreateAsync(AdminActor actor, ViewTemplateContent content, CancellationToken cancellationToken)
    {
        if (await db.ViewTemplates.CountAsync(cancellationToken) >= ViewTemplateLimits.MaxTemplates)
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.LimitReached);
        }

        var template = new ViewTemplate { CreatedByUserName = actor.UserName, UpdatedByUserName = actor.UserName };
        Apply(template, content);
        db.ViewTemplates.Add(template);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.TemplateCreated, AdminAuditObjects.ViewTemplate, template.Id, null, AdminStoreSupport.WidgetCount(template.Layout)));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NameTaken);
        }

        await transaction.CommitAsync(cancellationToken);
        await db.Entry(template).ReloadAsync(cancellationToken);
        return new ViewTemplateWriteResult(ViewTemplateWriteStatus.Saved, Describe(template));
    }

    public async Task<ViewTemplateWriteResult> UpdateAsync(AdminActor actor, int templateId, uint expectedVersion, ViewTemplateContent content, CancellationToken cancellationToken)
    {
        ViewTemplate? template = await db.ViewTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NotFound);
        }

        // The version the editor loaded, not the one just read: a save made from a stale copy must fail.
        db.Entry(template).Property(t => t.RowVersion).OriginalValue = expectedVersion;
        Apply(template, content);
        template.UpdatedByUserName = actor.UserName;
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.TemplateUpdated, AdminAuditObjects.ViewTemplate, template.Id, null, AdminStoreSupport.WidgetCount(template.Layout)));
        return await SaveAsync(template, cancellationToken);
    }

    public async Task<ViewTemplateWriteResult> SetPublishedAsync(AdminActor actor, int templateId, bool published, CancellationToken cancellationToken)
    {
        ViewTemplate? template = await db.ViewTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NotFound);
        }

        template.IsPublished = published;
        template.UpdatedByUserName = actor.UserName;
        string action = published ? AdminAuditActions.TemplatePublished : AdminAuditActions.TemplateUnpublished;
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(action, AdminAuditObjects.ViewTemplate, template.Id));
        return await SaveAsync(template, cancellationToken);
    }

    public async Task<ViewTemplateWriteResult> DeleteAsync(AdminActor actor, int templateId, CancellationToken cancellationToken)
    {
        if (!await db.ViewTemplates.AnyAsync(t => t.Id == templateId, cancellationToken))
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NotFound);
        }

        if (await db.TemplateAssignments.AnyAsync(a => a.ViewTemplateId == templateId, cancellationToken))
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.HasAssignments);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int removed = await db.ViewTemplates.Where(t => t.Id == templateId).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NotFound);
        }

        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.TemplateDeleted, AdminAuditObjects.ViewTemplate, templateId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ViewTemplateWriteResult(ViewTemplateWriteStatus.Saved);
    }

    private async Task<ViewTemplateWriteResult> SaveAsync(ViewTemplate template, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.Conflict);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new ViewTemplateWriteResult(ViewTemplateWriteStatus.NameTaken);
        }

        await db.Entry(template).ReloadAsync(cancellationToken);
        return new ViewTemplateWriteResult(ViewTemplateWriteStatus.Saved, Describe(template));
    }

    private static void Apply(ViewTemplate template, ViewTemplateContent content)
    {
        template.Name = content.Name;
        template.Description = string.IsNullOrWhiteSpace(content.Description) ? null : content.Description;
        template.Layout = AdminStoreSupport.Serialize(content.Layout);
        template.LayoutSchemaVersion = content.Layout.Version;
    }

    private static ViewTemplateDetail Describe(ViewTemplate template) =>
        new(template.Id, template.Name, template.Description, template.IsPublished, AdminStoreSupport.ReadLayout(template.Layout), template.RowVersion, template.UpdatedAtTimestamp);
}
