using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Auth.Sso;

/// <summary>Ends the session: revokes it so a copied cookie stops working, clears the cookie, and sends the browser to the portal logout.</summary>
internal static class LogoutEndpoint
{
    public static async Task<IResult> Handle(
        HttpContext context,
        IOptions<PrismAuthOptions> options,
        AuthCookies cookies,
        ISessionRevocationStore revocations,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        PrismAuthOptions settings = options.Value;

        // The authentication middleware has already validated the cookie, so context.User is trustworthy here.
        string? sessionId = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (sessionId is not null)
        {
            // If the expiry claim is somehow unreadable, assume the longest life a session can have rather than skip revoking.
            DateTimeOffset expiresAt = long.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Exp), out long unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : timeProvider.GetUtcNow().AddMinutes(settings.SessionLifetimeMinutes);
            await revocations.RevokeAsync(
                sessionId,
                context.User.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "",
                expiresAt,
                cancellationToken);
        }

        context.Response.Cookies.Delete(cookies.Session, cookies.DeleteOptions(cookies.SessionOptions()));

        // The origin comes from configuration. The Host header is accepted only in Development, where there is no proxy to vouch for it.
        string origin = settings.PublicBaseUrl.Length > 0
            ? settings.PublicBaseUrl.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}";
        string returnUrl = $"{origin}/login";
        string url = $"{settings.AccountPortalBaseUrl.TrimEnd('/')}/logout?appId={Uri.EscapeDataString(settings.ApplicationId)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.Redirect(url);
    }
}
