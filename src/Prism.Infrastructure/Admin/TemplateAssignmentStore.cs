using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Admin;

/// <summary>Assignments in Prism's own database. A push keeps a snapshot of the template, and each change is saved with its audit record in one transaction.</summary>
public sealed class TemplateAssignmentStore(PrismDbContext db, TimeProvider timeProvider) : ITemplateAssignmentStore
{
    public async Task<AssignmentPreview?> PreviewAsync(int templateId, AssignmentScope scope, CancellationToken cancellationToken)
    {
        ViewTemplate? template = await db.ViewTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
        {
            return null;
        }

        int? providers = scope.Kind == AssignmentScopeKinds.Provider ? 1 : null;
        return new AssignmentPreview(template.Id, template.Name, AdminStoreSupport.WidgetCount(template.Layout), scope, providers);
    }

    public async Task<IReadOnlyList<AssignmentSummary>> ListAsync(int templateId, CancellationToken cancellationToken)
    {
        List<TemplateAssignment> assignments = await db.TemplateAssignments.AsNoTracking()
            .Where(a => a.ViewTemplateId == templateId)
            .OrderByDescending(a => a.Id)
            .Take(ViewTemplateLimits.MaxAssignmentsPerTemplate)
            .ToListAsync(cancellationToken);
        int[] ids = [.. assignments.Select(a => a.Id)];
        Dictionary<int, int> delivered = await db.TemplateDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.TemplateAssignmentId))
            .GroupBy(d => d.TemplateAssignmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken);
        return [.. assignments.Select(a => Describe(a, delivered.GetValueOrDefault(a.Id)))];
    }

    public async Task<AssignmentWriteResult> CreateAsync(AdminActor actor, int templateId, AssignmentScope scope, int confirmedWidgetCount, CancellationToken cancellationToken)
    {
        ViewTemplate? template = await db.ViewTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
        {
            return new AssignmentWriteResult(AssignmentWriteStatus.TemplateNotFound);
        }

        if (!template.IsPublished)
        {
            return new AssignmentWriteResult(AssignmentWriteStatus.NotPublished);
        }

        int widgets = AdminStoreSupport.WidgetCount(template.Layout);
        if (widgets != confirmedWidgetCount)
        {
            return new AssignmentWriteResult(AssignmentWriteStatus.StaleConfirmation);
        }

        if (await db.TemplateAssignments.CountAsync(a => a.ViewTemplateId == templateId, cancellationToken) >= ViewTemplateLimits.MaxAssignmentsPerTemplate)
        {
            return new AssignmentWriteResult(AssignmentWriteStatus.LimitReached);
        }

        var assignment = new TemplateAssignment
        {
            ViewTemplateId = template.Id,
            ScopeKind = scope.Kind,
            ProviderId = scope.ProviderId,
            Name = template.Name,
            Description = template.Description,
            Layout = template.Layout,
            LayoutSchemaVersion = template.LayoutSchemaVersion,
            CreatedByPortalUserId = actor.PortalUserId,
            CreatedByUserName = actor.UserName,
            UpdatedByUserName = actor.UserName,
        };
        db.TemplateAssignments.Add(assignment);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.AssignmentCreated, AdminAuditObjects.TemplateAssignment, assignment.Id, scope.ProviderId, widgets));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await db.Entry(assignment).ReloadAsync(cancellationToken);
        return new AssignmentWriteResult(AssignmentWriteStatus.Saved, Describe(assignment, 0));
    }

    public async Task<AssignmentWriteResult> RevokeAsync(AdminActor actor, int assignmentId, CancellationToken cancellationToken)
    {
        TemplateAssignment? assignment = await db.TemplateAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
        {
            return new AssignmentWriteResult(AssignmentWriteStatus.NotFound);
        }

        if (assignment.RevokedAtTimestamp is null)
        {
            assignment.RevokedAtTimestamp = timeProvider.GetUtcNow();
            assignment.UpdatedByUserName = actor.UserName;
            AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.AssignmentRevoked, AdminAuditObjects.TemplateAssignment, assignment.Id, assignment.ProviderId));
            await db.SaveChangesAsync(cancellationToken);
        }

        int delivered = await db.TemplateDeliveries.CountAsync(d => d.TemplateAssignmentId == assignmentId, cancellationToken);
        return new AssignmentWriteResult(AssignmentWriteStatus.Saved, Describe(assignment, delivered));
    }

    private static AssignmentSummary Describe(TemplateAssignment assignment, int delivered) =>
        new(
            assignment.Id,
            assignment.ViewTemplateId,
            new AssignmentScope(assignment.ScopeKind, assignment.ProviderId),
            assignment.Name,
            AdminStoreSupport.WidgetCount(assignment.Layout),
            delivered,
            assignment.CreatedAtTimestamp,
            assignment.RevokedAtTimestamp);
}
