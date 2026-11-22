using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.SavedViews;

/// <summary>Saved Views in Prism's own database. Every query is scoped by the Provider and the user the caller passes in.</summary>
public sealed class SavedViewStore(PrismDbContext db) : ISavedViewStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SavedViewLists> ListAsync(SavedViewActor actor, CancellationToken cancellationToken)
    {
        List<SavedView> mine = await db.SavedViews.AsNoTracking()
            .Where(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId)
            .OrderBy(v => v.Name)
            .Take(SavedViewLimits.MaxListedViews)
            .ToListAsync(cancellationToken);

        List<SavedView> shared = await (
            from share in db.SavedViewShares.AsNoTracking()
            join view in db.SavedViews.AsNoTracking() on share.SavedViewId equals view.Id
            where share.SharedWithPortalUserId == actor.PortalUserId && view.ProviderId == actor.ProviderId
            orderby view.Name
            select view)
            .Take(SavedViewLimits.MaxListedViews)
            .ToListAsync(cancellationToken);

        return new SavedViewLists([.. mine.Select(v => Summarize(v, true))], [.. shared.Select(v => Summarize(v, false))]);
    }

    public async Task<SavedViewDetail?> GetAsync(SavedViewActor actor, int viewId, CancellationToken cancellationToken)
    {
        SavedView? view = await db.SavedViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == viewId && v.ProviderId == actor.ProviderId, cancellationToken);
        if (view is null)
        {
            return null;
        }

        bool isOwner = view.OwnerPortalUserId == actor.PortalUserId;
        if (!isOwner && !await db.SavedViewShares.AsNoTracking().AnyAsync(s => s.SavedViewId == viewId && s.SharedWithPortalUserId == actor.PortalUserId, cancellationToken))
        {
            return null;
        }

        return Describe(view, isOwner);
    }

    public async Task<SavedViewWriteResult> CreateAsync(SavedViewActor actor, SavedViewContent content, CancellationToken cancellationToken)
    {
        int owned = await db.SavedViews.CountAsync(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId, cancellationToken);
        if (owned >= SavedViewLimits.MaxViewsPerOwner)
        {
            return new SavedViewWriteResult(SavedViewWriteStatus.LimitReached);
        }

        var view = new SavedView
        {
            ProviderId = actor.ProviderId,
            OwnerPortalUserId = actor.PortalUserId,
            CreatedByUserName = actor.UserName,
            UpdatedByUserName = actor.UserName,
        };
        Apply(view, content);
        db.SavedViews.Add(view);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(view).State = EntityState.Detached;
            return new SavedViewWriteResult(SavedViewWriteStatus.NameTaken);
        }

        await db.Entry(view).ReloadAsync(cancellationToken);
        return new SavedViewWriteResult(SavedViewWriteStatus.Saved, Describe(view, true));
    }

    public async Task<SavedViewWriteResult> UpdateAsync(SavedViewActor actor, int viewId, uint expectedVersion, SavedViewContent content, CancellationToken cancellationToken)
    {
        SavedView? view = await db.SavedViews.FirstOrDefaultAsync(
            v => v.Id == viewId && v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId, cancellationToken);
        if (view is null)
        {
            return new SavedViewWriteResult(SavedViewWriteStatus.NotFound);
        }

        // The version the editor loaded, not the one just read: a save made from a stale copy must fail.
        db.Entry(view).Property(v => v.RowVersion).OriginalValue = expectedVersion;
        Apply(view, content);
        view.UpdatedByUserName = actor.UserName;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(view).State = EntityState.Detached;
            return new SavedViewWriteResult(SavedViewWriteStatus.Conflict);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(view).State = EntityState.Detached;
            return new SavedViewWriteResult(SavedViewWriteStatus.NameTaken);
        }

        await db.Entry(view).ReloadAsync(cancellationToken);
        return new SavedViewWriteResult(SavedViewWriteStatus.Saved, Describe(view, true));
    }

    public async Task<bool> DeleteAsync(SavedViewActor actor, int viewId, CancellationToken cancellationToken)
    {
        if (!await db.SavedViews.AnyAsync(v => v.Id == viewId && v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId, cancellationToken))
        {
            return false;
        }

        // Foreign keys restrict deletes, so the application removes the children first, in one transaction.
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SavedViewShares.Where(s => s.SavedViewId == viewId).ExecuteDeleteAsync(cancellationToken);
        await db.SavedViewInvitations.Where(i => i.SavedViewId == viewId).ExecuteDeleteAsync(cancellationToken);
        int removed = await db.SavedViews.Where(v => v.Id == viewId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return removed == 1;
    }

    private static void Apply(SavedView view, SavedViewContent content)
    {
        view.Name = content.Name;
        view.Description = string.IsNullOrWhiteSpace(content.Description) ? null : content.Description;
        // Written from the validated object, so only the known properties are ever stored.
        view.Layout = JsonSerializer.Serialize(content.Layout, Json);
        view.LayoutSchemaVersion = content.Layout.Version;
    }

    private static SavedViewSummary Summarize(SavedView view, bool isOwner) =>
        new(view.Id, view.Name, view.Description, view.CreatedByUserName, isOwner, ReadLayout(view.Layout).Widgets?.Count ?? 0, view.UpdatedAtTimestamp);

    private static SavedViewDetail Describe(SavedView view, bool isOwner) =>
        new(view.Id, view.Name, view.Description, view.CreatedByUserName, isOwner, ReadLayout(view.Layout), view.RowVersion, view.UpdatedAtTimestamp);

    private static SavedViewLayout ReadLayout(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SavedViewLayout>(json, Json) ?? Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    private static SavedViewLayout Empty() => new(SavedViewLimits.CurrentLayoutVersion, "comfortable", []);

    internal static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
