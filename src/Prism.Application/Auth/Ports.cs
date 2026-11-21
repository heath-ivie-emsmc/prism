namespace Prism.Application.Auth;

/// <summary>Reads the Admin Portal. The nonce from a login token can be exchanged exactly once.</summary>
public interface IPortalDirectory
{
    Task<PortalUser> ResolveUserAsync(string nonce, CancellationToken cancellationToken);

    /// <summary>The Providers (companies) a portal user is assigned to, with Facility limits and names. Not part of sign-in: it can be large.</summary>
    Task<IReadOnlyList<ProviderScope>> ResolveProvidersAsync(long portalUserId, CancellationToken cancellationToken);
}

/// <summary>
/// The Providers a signed-in user may see. Read from the portal and remembered for a few minutes, never kept in the session cookie:
/// a user can have thousands of Facilities, and a revoked assignment should stop working within minutes, not hours.
/// </summary>
public interface IUserScopeProvider
{
    Task<IReadOnlyList<ProviderScope>> GetProvidersAsync(long portalUserId, CancellationToken cancellationToken);
}

/// <summary>Fetches the PEM public key the portal signs login tokens with.</summary>
public interface IPortalPublicKeyProvider
{
    Task<string> GetPublicKeyPemAsync(CancellationToken cancellationToken);
}

public interface ILoginTokenValidator
{
    /// <exception cref="LoginTokenInvalidException">The token is not acceptable.</exception>
    Task<ValidatedLoginToken> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed record ValidatedLoginToken(string TokenId, DateTimeOffset ExpiresAt);

/// <summary>Remembers login token ids so a token cannot be used twice.</summary>
public interface ILoginTokenReplayStore
{
    /// <returns>True the first time an id is seen, false for a replay.</returns>
    ValueTask<bool> TryRememberAsync(string tokenId, TimeSpan window, CancellationToken cancellationToken);
}

public interface ISessionTokenIssuer
{
    /// <summary>Issues the session JWT for a user. Throws <see cref="SessionTooLargeException"/> if it will not fit in a cookie.</summary>
    string Issue(PortalUser user, IReadOnlyList<string> permissions);
}

/// <summary>Durable list of sessions that were ended before they expired, so sign-out and revocation hold on live sessions.</summary>
public interface ISessionRevocationStore
{
    /// <summary>Revokes a session. Revoking the same session again is not an error.</summary>
    Task RevokeAsync(string sessionId, string userName, DateTimeOffset sessionExpiresAt, CancellationToken cancellationToken);

    Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken);
}
