using Prism.Api.Auth.Identity;
using Prism.Api.Auth.Sso;

namespace Prism.Api.Auth;

/// <summary>Wires the sign-in and identity routes. The handlers live next to the concern they serve, under <c>Sso</c> and <c>Identity</c>.</summary>
public static class AuthEndpoints
{
    /// <summary>Starting a sign-in or signing out.</summary>
    public const string RateLimitPolicy = "auth";

    /// <summary>Routes that consume a login token. Tighter, because a real user needs one or two of these a minute.</summary>
    public const string CallbackRateLimitPolicy = "auth-callback";

    /// <summary>Maps the browser sign-in handshake onto the /api group. It is public by necessity and rate limited.</summary>
    public static void MapSsoEndpoints(this RouteGroupBuilder api)
    {
        // Browser redirects and a form post, not a JSON API: they are left out of the OpenAPI document on purpose.
        RouteGroupBuilder sso = api.MapGroup("/auth/sso").AllowAnonymous();
        sso.MapGet("/start", StartEndpoint.Handle).RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();
        sso.MapGet("/logout", LogoutEndpoint.Handle).RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();
        sso.MapGet("/callback", CallbackEndpoints.Callback).RequireRateLimiting(CallbackRateLimitPolicy).ExcludeFromDescription();
        sso.MapPost("/confirm", CallbackEndpoints.Confirm).RequireRateLimiting(CallbackRateLimitPolicy).ExcludeFromDescription();
    }

    /// <summary>Maps the identity routes that need a signed-in user.</summary>
    public static void MapIdentityEndpoints(this RouteGroupBuilder api) => MeEndpoint.Map(api);
}
