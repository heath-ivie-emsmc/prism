using Microsoft.Extensions.Options;
using Prism.Application.SavedViews;
using Prism.Infrastructure.SavedViews;

namespace Prism.Api.SavedViews;

public enum InviteStatus
{
    Sent,
    NotFound,
    Disabled,
    TooManyRecipients,
    TooManyInvitations,
    NothingSent,
}

public sealed record InviteResult(InviteStatus Status, IReadOnlyList<PendingInvitation> Sent, IReadOnlyList<string> Failed);

/// <summary>
/// Makes the invitations for a view and emails each link. The link is built from the configured public address, never from the request. If an email
/// cannot be sent its invitation is revoked at once, so no live token exists that nobody received. Nothing here logs an address or a token.
/// </summary>
public sealed class InvitationService(
    ISavedViewStore views,
    ISavedViewInvitationStore invitations,
    IInvitationEmailSender sender,
    IOptions<SavedViewInvitationOptions> options)
{
    public async Task<InviteResult> InviteAsync(SavedViewActor owner, int viewId, IReadOnlyList<string> emails, CancellationToken cancellationToken)
    {
        SavedViewInvitationOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return new InviteResult(InviteStatus.Disabled, [], []);
        }

        SavedViewDetail? view = await views.GetAsync(owner, viewId, cancellationToken);
        if (view is not { IsOwner: true })
        {
            return new InviteResult(InviteStatus.NotFound, [], []);
        }

        InvitationCreateResult created = await invitations.CreateAsync(owner, viewId, emails, TimeSpan.FromDays(settings.ExpiryDays), cancellationToken);
        if (created.Status != InvitationCreateStatus.Created)
        {
            return new InviteResult(Map(created.Status), [], []);
        }

        List<PendingInvitation> sent = [];
        List<string> failed = [];
        foreach (NewInvitation invitation in created.Invitations)
        {
            if (await TrySendAsync(owner, view, settings, invitation, cancellationToken))
            {
                sent.Add(new PendingInvitation(invitation.InvitationId, invitation.Email, invitation.ExpiresAt, false));
                continue;
            }

            await invitations.RevokeInvitationAsync(owner, viewId, invitation.InvitationId, cancellationToken);
            failed.Add(invitation.Email);
        }

        return new InviteResult(sent.Count > 0 ? InviteStatus.Sent : InviteStatus.NothingSent, sent, failed);
    }

    private async Task<bool> TrySendAsync(SavedViewActor owner, SavedViewDetail view, SavedViewInvitationOptions settings, NewInvitation invitation, CancellationToken cancellationToken)
    {
        string link = $"{settings.PublicBaseUrl.TrimEnd('/')}/saved-view-invitations/redeem?token={invitation.Token}";
        try
        {
            await sender.SendAsync(new InvitationEmail(invitation.Email, view.Name, owner.UserName, link, invitation.ExpiresAt), cancellationToken);
            return true;
        }
        catch (InvitationEmailUnavailableException)
        {
            return false;
        }
    }

    private static InviteStatus Map(InvitationCreateStatus status) => status switch
    {
        InvitationCreateStatus.TooManyRecipients => InviteStatus.TooManyRecipients,
        InvitationCreateStatus.TooManyInvitations => InviteStatus.TooManyInvitations,
        _ => InviteStatus.NotFound,
    };
}
