using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Auth.Sso;

/// <summary>Begins a sign-in: remembers where to land afterwards, sets the state cookie, and redirects to the Account Portal.</summary>
internal static class StartEndpoint
{
    public static IResult Handle(
        HttpContext context,
        IOptions<PrismAuthOptions> options,
        AuthCookies cookies,
        string? returnUrl,
        string? reauth)
    {
        PrismAuthOptions settings = options.Value;

        string? safeReturn = RedirectTargets.TryNormalize(returnUrl);
        if (safeReturn is not null)
        {
            context.Response.Cookies.Append(AuthCookies.ReturnUrlCookie, safeReturn, cookies.ReturnUrlOptions());
        }
        else if (context.Request.Cookies.ContainsKey(AuthCookies.ReturnUrlCookie))
        {
            // A new sign-in without a (safe) target must not inherit the target of an earlier abandoned one.
            context.Response.Cookies.Delete(AuthCookies.ReturnUrlCookie, cookies.DeleteOptions(cookies.ReturnUrlOptions()));
        }

        string state = SecureToken.Hex(32);
        context.Response.Cookies.Append(cookies.State, state, cookies.StateOptions());

        string url = $"{settings.AccountPortalBaseUrl.TrimEnd('/')}/?appId={Uri.EscapeDataString(settings.ApplicationId)}&state={Uri.EscapeDataString(state)}";
        if (!string.IsNullOrWhiteSpace(settings.LoginReturnUrl))
        {
            // Local development: the portal sends the token to this registered development redirect, not the application's login URL.
            url = $"{settings.AccountPortalBaseUrl.TrimEnd('/')}/login?appId={Uri.EscapeDataString(settings.ApplicationId)}&returnUrl={Uri.EscapeDataString(settings.LoginReturnUrl)}&state={Uri.EscapeDataString(state)}";
        }

        bool forceAuth = reauth is "1" || string.Equals(reauth, "true", StringComparison.OrdinalIgnoreCase);
        string forceQuery = settings.AccountPortalForceAuthQuery.TrimStart('?', '&');
        if (forceAuth && forceQuery.Length > 0)
        {
            url += "&" + forceQuery;
        }

        return Results.Redirect(url);
    }
}
