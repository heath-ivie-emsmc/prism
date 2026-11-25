using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Auth;

/// <summary>Turns a portal login token into a session token: validate, refuse replays, exchange the nonce, map permissions, issue.</summary>
public sealed class SignInService(
    ILoginTokenValidator validator,
    ILoginTokenReplayStore replayStore,
    IPortalDirectory directory,
    ISessionTokenIssuer issuer,
    IOptions<PrismAuthOptions> options,
    IOptions<InternalAccessOptions> internalOptions,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MinReplayWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MaxReplayWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    /// <exception cref="LoginTokenInvalidException">The token was rejected.</exception>
    /// <exception cref="LoginTokenReplayedException">The token was already used.</exception>
    /// <exception cref="MissingRolesException">The user has no identity or no role for this application.</exception>
    /// <exception cref="SessionTooLargeException">The user's assignments do not fit in a session cookie.</exception>
    public async Task<SignInResult> SignInAsync(string loginToken, CancellationToken cancellationToken)
    {
        ValidatedLoginToken token = await validator.ValidateAsync(loginToken, cancellationToken);

        TimeSpan window = token.ExpiresAt - timeProvider.GetUtcNow() + ClockSkew;
        window = TimeSpan.FromTicks(Math.Clamp(window.Ticks, MinReplayWindow.Ticks, MaxReplayWindow.Ticks));
        if (!await replayStore.TryRememberAsync(token.TokenId, window, cancellationToken))
        {
            throw new LoginTokenReplayedException();
        }

        PortalUser user = await directory.ResolveUserAsync(token.TokenId, cancellationToken);
        IReadOnlyList<string> permissions = Permissions.ForRoles(user.Roles, [options.Value.ApplicationId, .. options.Value.RolePrefixes]);
        if (string.IsNullOrWhiteSpace(user.UserName) || !permissions.Contains(Permissions.App))
        {
            throw new MissingRolesException(!string.IsNullOrWhiteSpace(user.UserName), user.Roles);
        }

        bool isInternal = InternalAccess.IsInternal(user.Groups, internalOptions.Value.GroupName, permissions);
        // Customer permissions are not in the session: they come from the user's Customer Role in each Provider. The session keeps sign-in and the admin tiers.
        IReadOnlyList<string> sessionPermissions = [.. permissions.Where(p => p == Permissions.App || p == Permissions.InternalUser || Permissions.AdminTiers.Contains(p))];
        string session = issuer.Issue(user, InternalAccess.Filter(sessionPermissions, isInternal));
        return new SignInResult(session, user.Roles, permissions.Count, isInternal);
    }
}

/// <summary>The session token, plus what the sign-in decided, for a log line of counts and flags.</summary>
public sealed record SignInResult(string Session, IReadOnlyList<string> PortalRoles, int KnownPermissionCount, bool IsInternal);
