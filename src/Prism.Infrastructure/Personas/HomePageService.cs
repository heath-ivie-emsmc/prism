using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Prism.Application.Personas;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Personas;

/// <summary>
/// A user's home page in one Provider. The Persona's published View Template is followed live until the user customizes it, which copies it into
/// their own Saved Views. The Provider and user come from server code, so a copy is only ever in the user's own scope.
/// </summary>
public sealed class HomePageService(PrismDbContext db, ISavedViewStore views) : IHomePage
{
    private const string HomeName = "Home";

    public async Task<HomeResolution> ResolveAsync(SavedViewActor actor, CancellationToken cancellationToken)
    {
        int? copyId = await CopyIdAsync(actor, cancellationToken);
        if (copyId is not null)
        {
            return new HomeResolution(HomeSources.Custom, SavedViewId: copyId);
        }

        ViewTemplate? template = await PersonaTemplateAsync(actor.PortalUserId, cancellationToken);
        return template is null
            ? new HomeResolution(HomeSources.Default)
            : new HomeResolution(HomeSources.Persona, AdminStoreSupport.ReadLayout(template.Layout));
    }

    public async Task<HomeCustomizeResult> CustomizeAsync(SavedViewActor actor, CancellationToken cancellationToken)
    {
        if (await CopyIdAsync(actor, cancellationToken) is { } existing)
        {
            return new HomeCustomizeResult(HomeCustomizeStatus.Customized, existing);
        }

        ViewTemplate? template = await PersonaTemplateAsync(actor.PortalUserId, cancellationToken);
        if (template is null)
        {
            return new HomeCustomizeResult(HomeCustomizeStatus.NothingToCustomize);
        }

        List<string> taken = await db.SavedViews.AsNoTracking()
            .Where(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId)
            .Select(v => v.Name)
            .ToListAsync(cancellationToken);
        if (taken.Count >= SavedViewLimits.MaxViewsPerOwner || TemplateDeliveryService.PickName(HomeName, [.. taken]) is not { } name)
        {
            return new HomeCustomizeResult(HomeCustomizeStatus.LimitReached);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var view = new SavedView
            {
                ProviderId = actor.ProviderId,
                OwnerPortalUserId = actor.PortalUserId,
                Name = name,
                Layout = template.Layout,
                LayoutSchemaVersion = template.LayoutSchemaVersion,
                CreatedByUserName = actor.UserName,
                UpdatedByUserName = actor.UserName,
            };
            db.SavedViews.Add(view);
            await db.SaveChangesAsync(cancellationToken);
            db.UserHomeViews.Add(new UserHomeView { PortalUserId = actor.PortalUserId, ProviderId = actor.ProviderId, SavedViewId = view.Id });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HomeCustomizeResult(HomeCustomizeStatus.Customized, view.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A second tab customized first: that copy is the answer.
            db.ChangeTracker.Clear();
            return await CopyIdAsync(actor, cancellationToken) is { } id
                ? new HomeCustomizeResult(HomeCustomizeStatus.Customized, id)
                : new HomeCustomizeResult(HomeCustomizeStatus.LimitReached);
        }
    }

    public async Task<bool> ResetAsync(SavedViewActor actor, CancellationToken cancellationToken)
    {
        if (await CopyIdAsync(actor, cancellationToken) is not { } id)
        {
            return false;
        }

        // Deleting the copy also forgets it as the home page.
        return await views.DeleteAsync(actor, id, cancellationToken);
    }

    private Task<int?> CopyIdAsync(SavedViewActor actor, CancellationToken cancellationToken) =>
        db.UserHomeViews.AsNoTracking()
            .Where(h => h.PortalUserId == actor.PortalUserId && h.ProviderId == actor.ProviderId)
            .Select(h => (int?)h.SavedViewId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The user's Persona's template, only while both are published.</summary>
    private Task<ViewTemplate?> PersonaTemplateAsync(long portalUserId, CancellationToken cancellationToken) =>
        (from profile in db.UserProfiles.AsNoTracking()
         join persona in db.Personas.AsNoTracking() on profile.PersonaId equals persona.Id
         join template in db.ViewTemplates.AsNoTracking() on persona.ViewTemplateId equals template.Id
         where profile.PortalUserId == portalUserId && persona.IsPublished && template.IsPublished
         select template).FirstOrDefaultAsync(cancellationToken);
}
