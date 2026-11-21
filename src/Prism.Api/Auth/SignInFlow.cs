using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Auth;

/// <summary>Finishes a sign-in for the callback and confirm routes: runs it, sets the cookies, picks the redirect, and maps failures to short codes.</summary>
public sealed class SignInFlow(
    SignInService signIn,
    AuthCookies cookies,
    IOptions<PrismAuthOptions> options,
    ILogger<SignInFlow> logger)
{
    public bool AllowIdpInitiated => options.Value.AllowIdpInitiated;

    /// <summary>Redirect to the SPA login page with a code. The SPA maps codes to messages, so no cause is ever shown from the browser's side.</summary>
    public static IResult Failed(string code) => Results.LocalRedirect($"/login?error={code}");

    public async Task<IResult> CompleteAsync(HttpContext context, string loginToken, CancellationToken cancellationToken)
    {
        string session;
        try
        {
            session = await signIn.SignInAsync(loginToken, cancellationToken);
        }
        catch (LoginTokenInvalidException ex)
        {
            AuthLog.LoginTokenRejected(logger, ex, ex.Failure);
            return Failed(ex.Failure == LoginTokenFailure.Expired ? "sso_expired" : "sso_failed");
        }
        catch (LoginTokenReplayedException ex)
        {
            AuthLog.LoginTokenReplayed(logger, ex);
            return Failed("sso_failed");
        }
        catch (MissingRolesException ex)
        {
            AuthLog.NoRole(logger, ex, ex.HasUserName, ex.Roles.Count);
            // Role names come from outside: Debug only, capped in count and length.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                AuthLog.NoRoleNames(logger, string.Join(", ", ex.Roles.Take(20).Select(role => role.Length <= 100 ? role : role[..100])));
            }

            return Failed("access_denied");
        }
        catch (SessionTooLargeException ex)
        {
            AuthLog.SessionTooLarge(logger, ex, ex.Length);
            return Failed("sso_failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Portal outage, malformed portal response or bad configuration: fail closed.
            AuthLog.SignInFailed(logger, ex, ex.GetType().Name);
            return Failed("sso_failed");
        }

        context.Response.Cookies.Append(cookies.Session, session, cookies.SessionOptions());
        // A new session gets a new CSRF token.
        context.Response.Cookies.Append(AuthCookies.XsrfCookie, SecureToken.Base64(32), cookies.XsrfOptions());

        string? returnUrl = RedirectTargets.TryNormalize(context.Request.Cookies[AuthCookies.ReturnUrlCookie]);
        context.Response.Cookies.Delete(AuthCookies.ReturnUrlCookie, cookies.DeleteOptions(cookies.ReturnUrlOptions()));
        return Results.LocalRedirect(returnUrl ?? RedirectTargets.TryNormalize(options.Value.PostLoginRedirectUrl) ?? "/");
    }
}
