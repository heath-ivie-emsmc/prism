namespace Prism.Application.SavedViews;

/// <summary>A person with access to a view, as the owner sees them. <paramref name="Email"/> is the address the invitation went to, when there was one.</summary>
public sealed record SavedViewRecipient(int ShareId, string? Email, DateTimeOffset SharedAt);

/// <summary>An invitation that has not been redeemed or revoked.</summary>
public sealed record PendingInvitation(int InvitationId, string Email, DateTimeOffset ExpiresAt, bool IsExpired);

public sealed record SavedViewSharing(IReadOnlyList<SavedViewRecipient> Recipients, IReadOnlyList<PendingInvitation> Pending);

/// <summary>The result of making invitations for a list of addresses.</summary>
public enum InvitationCreateStatus
{
    Created,
    NotFound,
    TooManyRecipients,
    TooManyInvitations,
}

public sealed record NewInvitation(int InvitationId, string Email, DateTimeOffset ExpiresAt, string Token);

public sealed record InvitationCreateResult(InvitationCreateStatus Status, IReadOnlyList<NewInvitation> Invitations);

/// <summary>Where an invitation leads, found without using it up.</summary>
public sealed record InvitationTarget(int ProviderId, int SavedViewId);

/// <summary>
/// Emailed invitations to open a view. The token is shown once, to the code that emails it; only its hash is stored. A token works once, until it expires,
/// and not after the owner revokes it. Every failure to redeem is the same answer, so nothing says whether a token, a view or an address exists.
/// </summary>
public interface ISavedViewInvitationStore
{
    /// <summary>Owner only. Replaces any pending invitation for the same address on the view.</summary>
    Task<InvitationCreateResult> CreateAsync(SavedViewActor owner, int viewId, IReadOnlyList<string> emails, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Owner only. Marks an invitation revoked, for instance when the email could not be sent.</summary>
    Task<bool> RevokeInvitationAsync(SavedViewActor owner, int viewId, int invitationId, CancellationToken cancellationToken);

    Task<SavedViewSharing?> ListSharingAsync(SavedViewActor owner, int viewId, CancellationToken cancellationToken);

    /// <summary>Owner only. Removes a recipient's access at once.</summary>
    Task<bool> RevokeShareAsync(SavedViewActor owner, int viewId, int shareId, CancellationToken cancellationToken);

    /// <summary>The Provider and view a live token points at, without using it up, or null when it is unknown, used, revoked or expired.</summary>
    Task<InvitationTarget?> FindTargetAsync(string token, CancellationToken cancellationToken);

    /// <summary>Uses the token up and gives <paramref name="portalUserId"/> access, in one step. Null when the token is not live.</summary>
    Task<InvitationTarget?> RedeemAsync(string token, long portalUserId, CancellationToken cancellationToken);
}

/// <summary>Tokens and their hashes. A token is 32 random bytes written as 43 URL-safe characters, and it is stored only as a SHA-256 hash.</summary>
public static class InvitationTokens
{
    public const int TokenLength = 43;

    public static string NewToken() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>True for exactly the shape <see cref="NewToken"/> makes. Checked before hashing, so a long or odd string costs nothing.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    public static string Hash(string token) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}

/// <summary>Addresses are trimmed and lower-cased, and checked for one @ with something either side. Real delivery is the only true test.</summary>
public static class InvitationEmails
{
    public static string? Normalize(string? email)
    {
        string trimmed = (email ?? "").Trim().ToLowerInvariant();
        if (trimmed.Length is 0 or > SavedViewLimits.MaxEmailLength || trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '<' or '>' or ',' or ';' or '"'))
        {
            return null;
        }

        int at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == trimmed.LastIndexOf('@') && at < trimmed.Length - 3 && trimmed.IndexOf('.', at) > at + 1 ? trimmed : null;
    }
}

/// <summary>What the invitation email says. Only the view name, who sent it and when it expires: no figures, no Provider data.</summary>
public sealed record InvitationEmail(string ToAddress, string ViewName, string SenderName, string Link, DateTimeOffset ExpiresAt);

/// <summary>Sends the invitation email. Throws <see cref="InvitationEmailUnavailableException"/> when it cannot, with nothing about the address in the message.</summary>
public interface IInvitationEmailSender
{
    Task SendAsync(InvitationEmail email, CancellationToken cancellationToken);
}

public sealed class InvitationEmailUnavailableException : Exception
{
    public InvitationEmailUnavailableException()
        : base("The invitation email could not be sent.")
    {
    }

    public InvitationEmailUnavailableException(string message)
        : base(message)
    {
    }

    public InvitationEmailUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
