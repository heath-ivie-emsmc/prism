using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.SavedViews;

/// <summary>Invitations and shares in Prism's own database. Time comes from the injected clock.</summary>
public sealed class SavedViewInvitationStore(PrismDbContext db, TimeProvider timeProvider) : ISavedViewInvitationStore
{
    /// <summary>How long an unused invitation that expired or was revoked stays in the table before it is purged.</summary>
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(30);

    public async Task<InvitationCreateResult> CreateAsync(SavedViewActor owner, int viewId, IReadOnlyList<string> emails, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(owner, viewId, cancellationToken))
        {
            return new InvitationCreateResult(InvitationCreateStatus.NotFound, []);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // Unused invitations that ran out or were revoked long ago do nothing any more. Redeemed ones stay: a share points at them.
        DateTimeOffset cutoff = now - PurgeAfter;
        await db.SavedViewInvitations
            .Where(i => i.RedeemedAtTimestamp == null && (i.ExpiresAtTimestamp < cutoff || (i.RevokedAtTimestamp != null && i.RevokedAtTimestamp < cutoff)))
            .ExecuteDeleteAsync(cancellationToken);

        string[] distinct = [.. emails.Distinct(StringComparer.Ordinal)];

        int recent = await db.SavedViewInvitations.CountAsync(i => i.CreatedByPortalUserId == owner.PortalUserId && i.CreatedAtTimestamp > now.AddHours(-1), cancellationToken);
        if (recent + distinct.Length > SavedViewLimits.MaxInvitationsPerOwnerPerHour)
        {
            return new InvitationCreateResult(InvitationCreateStatus.TooManyInvitations, []);
        }

        List<SavedViewInvitation> live = await db.SavedViewInvitations
            .Where(i => i.SavedViewId == viewId && i.RedeemedAtTimestamp == null && i.RevokedAtTimestamp == null && i.ExpiresAtTimestamp > now)
            .ToListAsync(cancellationToken);
        List<SavedViewInvitation> replaced = [.. live.Where(i => distinct.Contains(i.InvitedEmail, StringComparer.Ordinal))];
        int shares = await db.SavedViewShares.CountAsync(s => s.SavedViewId == viewId, cancellationToken);
        if (shares + live.Count - replaced.Count + distinct.Length > SavedViewLimits.MaxRecipientsPerView)
        {
            return new InvitationCreateResult(InvitationCreateStatus.TooManyRecipients, []);
        }

        foreach (SavedViewInvitation old in replaced)
        {
            old.RevokedAtTimestamp = now;
        }

        List<(SavedViewInvitation Row, string Token)> created = [];
        foreach (string email in distinct)
        {
            string token = InvitationTokens.NewToken();
            var row = new SavedViewInvitation
            {
                SavedViewId = viewId,
                ProviderId = owner.ProviderId,
                InvitedEmail = email,
                TokenHash = InvitationTokens.Hash(token),
                ExpiresAtTimestamp = now.Add(lifetime),
                CreatedByPortalUserId = owner.PortalUserId,
            };
            db.SavedViewInvitations.Add(row);
            created.Add((row, token));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InvitationCreateResult(InvitationCreateStatus.Created, [.. created.Select(c => new NewInvitation(c.Row.Id, c.Row.InvitedEmail, c.Row.ExpiresAtTimestamp, c.Token))]);
    }

    public async Task<bool> RevokeInvitationAsync(SavedViewActor owner, int viewId, int invitationId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(owner, viewId, cancellationToken))
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        int changed = await db.SavedViewInvitations
            .Where(i => i.Id == invitationId && i.SavedViewId == viewId && i.RedeemedAtTimestamp == null && i.RevokedAtTimestamp == null)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.RevokedAtTimestamp, now), cancellationToken);
        return changed == 1;
    }

    public async Task<SavedViewSharing?> ListSharingAsync(SavedViewActor owner, int viewId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(owner, viewId, cancellationToken))
        {
            return null;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<SavedViewRecipient> recipients = await (
            from share in db.SavedViewShares.AsNoTracking()
            where share.SavedViewId == viewId
            join invitation in db.SavedViewInvitations.AsNoTracking() on share.SavedViewInvitationId equals invitation.Id into invitations
            from invitation in invitations.DefaultIfEmpty()
            orderby share.CreatedAtTimestamp
            select new SavedViewRecipient(share.Id, invitation == null ? null : invitation.InvitedEmail, share.CreatedAtTimestamp))
            .Take(SavedViewLimits.MaxRecipientsPerView)
            .ToListAsync(cancellationToken);

        List<PendingInvitation> pending = await db.SavedViewInvitations.AsNoTracking()
            .Where(i => i.SavedViewId == viewId && i.RedeemedAtTimestamp == null && i.RevokedAtTimestamp == null)
            .OrderBy(i => i.InvitedEmail)
            .Take(SavedViewLimits.MaxRecipientsPerView)
            .Select(i => new PendingInvitation(i.Id, i.InvitedEmail, i.ExpiresAtTimestamp, i.ExpiresAtTimestamp <= now))
            .ToListAsync(cancellationToken);

        return new SavedViewSharing(recipients, pending);
    }

    public async Task<bool> RevokeShareAsync(SavedViewActor owner, int viewId, int shareId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(owner, viewId, cancellationToken))
        {
            return false;
        }

        return await db.SavedViewShares.Where(s => s.Id == shareId && s.SavedViewId == viewId).ExecuteDeleteAsync(cancellationToken) == 1;
    }

    public async Task<InvitationTarget?> FindTargetAsync(string token, CancellationToken cancellationToken)
    {
        if (!InvitationTokens.IsWellFormed(token))
        {
            return null;
        }

        string hash = InvitationTokens.Hash(token);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return await LiveTargets(hash, now).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<InvitationTarget?> RedeemAsync(string token, long portalUserId, CancellationToken cancellationToken)
    {
        if (!InvitationTokens.IsWellFormed(token))
        {
            return null;
        }

        string hash = InvitationTokens.Hash(token);
        DateTimeOffset now = timeProvider.GetUtcNow();

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        InvitationTarget? target = await LiveTargets(hash, now).FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return null;
        }

        bool isOwner = await db.SavedViews.AnyAsync(v => v.Id == target.SavedViewId && v.OwnerPortalUserId == portalUserId, cancellationToken);
        if (isOwner)
        {
            // The owner opening their own link needs nothing granted, and the link is left for the person it was meant for.
            return target;
        }

        // One conditional update decides the race: only one caller can move the row from unused to used.
        int used = await db.SavedViewInvitations
            .Where(i => i.TokenHash == hash && i.RedeemedAtTimestamp == null && i.RevokedAtTimestamp == null && i.ExpiresAtTimestamp > now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.RedeemedAtTimestamp, now).SetProperty(i => i.RedeemedByPortalUserId, portalUserId), cancellationToken);
        if (used != 1)
        {
            return null;
        }

        int invitationId = await db.SavedViewInvitations.Where(i => i.TokenHash == hash).Select(i => i.Id).SingleAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO saved_view_share (saved_view_id, shared_with_portal_user_id, saved_view_invitation_id)
            VALUES ({target.SavedViewId}, {portalUserId}, {invitationId})
            ON CONFLICT (saved_view_id, shared_with_portal_user_id) DO NOTHING
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return target;
    }

    // A live invitation whose view still exists in the same Provider.
    private IQueryable<InvitationTarget> LiveTargets(string hash, DateTimeOffset now) =>
        from invitation in db.SavedViewInvitations.AsNoTracking()
        where invitation.TokenHash == hash && invitation.RedeemedAtTimestamp == null && invitation.RevokedAtTimestamp == null && invitation.ExpiresAtTimestamp > now
        join view in db.SavedViews.AsNoTracking() on invitation.SavedViewId equals view.Id
        where view.ProviderId == invitation.ProviderId
        select new InvitationTarget(view.ProviderId, view.Id);

    private Task<bool> OwnsAsync(SavedViewActor owner, int viewId, CancellationToken cancellationToken) =>
        db.SavedViews.AsNoTracking().AnyAsync(v => v.Id == viewId && v.ProviderId == owner.ProviderId && v.OwnerPortalUserId == owner.PortalUserId, cancellationToken);
}
