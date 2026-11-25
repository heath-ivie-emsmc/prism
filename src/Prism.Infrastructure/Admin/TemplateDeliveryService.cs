using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Admin;

/// <summary>
/// Clones open Assignments into the user's Saved Views, once per Assignment, user and Provider. The Provider and owner are passed in by server code.
/// The delivery row is unique, so two requests at once deliver one copy.
/// </summary>
public sealed class TemplateDeliveryService(PrismDbContext db) : ITemplateDelivery
{
    /// <summary>The label shown as the creator of a delivered copy. It is not a person.</summary>
    public const string DeliveredBy = "Prism team";

    private const int NameAttempts = 5;

    public async Task<int> DeliverPendingAsync(SavedViewActor actor, CancellationToken cancellationToken)
    {
        List<TemplateAssignment> pending = await db.TemplateAssignments.AsNoTracking()
            .Where(a => a.RevokedAtTimestamp == null
                && (a.ScopeKind == AssignmentScopeKinds.All || (a.ScopeKind == AssignmentScopeKinds.Provider && a.ProviderId == actor.ProviderId))
                && !db.TemplateDeliveries.Any(d => d.TemplateAssignmentId == a.Id && d.ProviderId == actor.ProviderId && d.PortalUserId == actor.PortalUserId))
            .OrderBy(a => a.Id)
            .Take(ViewTemplateLimits.MaxDeliveriesPerRequest)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        int owned = await db.SavedViews.CountAsync(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId, cancellationToken);
        HashSet<string> names = (await db.SavedViews.AsNoTracking()
            .Where(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId)
            .Select(v => v.Name)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        int delivered = 0;
        foreach (TemplateAssignment assignment in pending)
        {
            if (owned >= SavedViewLimits.MaxViewsPerOwner || PickName(assignment.Name, names) is not { } name)
            {
                continue;
            }

            if (await TryDeliverAsync(actor, assignment, name, cancellationToken))
            {
                names.Add(name);
                owned++;
                delivered++;
            }
        }

        return delivered;
    }

    private async Task<bool> TryDeliverAsync(SavedViewActor actor, TemplateAssignment assignment, string name, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var view = new SavedView
            {
                ProviderId = actor.ProviderId,
                OwnerPortalUserId = actor.PortalUserId,
                Name = name,
                Description = assignment.Description,
                Layout = assignment.Layout,
                LayoutSchemaVersion = assignment.LayoutSchemaVersion,
                SourceTemplateAssignmentId = assignment.Id,
                CreatedByUserName = DeliveredBy,
                UpdatedByUserName = DeliveredBy,
            };
            db.SavedViews.Add(view);
            await db.SaveChangesAsync(cancellationToken);
            db.TemplateDeliveries.Add(new TemplateDelivery
            {
                TemplateAssignmentId = assignment.Id,
                ProviderId = actor.ProviderId,
                PortalUserId = actor.PortalUserId,
                SavedViewId = view.Id,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            // Another request delivered it first, or the name was taken a moment ago. Either way it is settled on the next open.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    internal static string? PickName(string baseName, HashSet<string> taken)
    {
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (int attempt = 2; attempt < 2 + NameAttempts; attempt++)
        {
            string suffix = $" ({attempt})";
            string candidate = (baseName.Length + suffix.Length <= SavedViewLimits.MaxNameLength ? baseName : baseName[..(SavedViewLimits.MaxNameLength - suffix.Length)]) + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
