using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Auth;

/// <summary>Cookie names and attributes for sign-in. With secure cookies the sensitive ones get the <c>__Host-</c> prefix (Path=/, no Domain).</summary>
public sealed class AuthCookies(IOptions<PrismAuthOptions> options, IHostEnvironment environment)
{
    public const string XsrfCookie = "XSRF-TOKEN";
    public const string XsrfHeader = "X-XSRF-TOKEN";
    public const string ReturnUrlCookie = "prism_sso_return";

    private static readonly TimeSpan HandshakeLifetime = TimeSpan.FromMinutes(10);

    public bool Secure => options.Value.RequireSecureCookies ?? environment.IsDeployedEnvironment();

    public string Session => Prefixed("prism_session");

    public string State => Prefixed("prism_sso_state");

    public string Confirm => Prefixed("prism_sso_confirm");

    /// <summary>Session cookie. Strict: it is only needed for same-site requests made by the SPA.</summary>
    public CookieOptions SessionOptions() => Build(SameSiteMode.Strict, TimeSpan.FromMinutes(options.Value.SessionLifetimeMinutes));

    /// <summary>State must survive the top-level redirect back from the portal, which is cross-site, so it is Lax.</summary>
    public CookieOptions StateOptions() => Build(SameSiteMode.Lax, HandshakeLifetime);

    /// <summary>The return URL also has to survive the redirect back from the portal.</summary>
    public CookieOptions ReturnUrlOptions() => Build(SameSiteMode.Lax, HandshakeLifetime);

    public CookieOptions ConfirmOptions() => Build(SameSiteMode.Strict, HandshakeLifetime);

    /// <summary>The XSRF cookie is the only one scripts may read.</summary>
    public CookieOptions XsrfOptions() => Build(SameSiteMode.Strict, TimeSpan.FromHours(24), httpOnly: false);

#pragma warning disable S2092 // Secure is true outside Development (RequireSecureCookies), and a deleting cookie must mirror the one it removes.
#pragma warning disable S3330 // The XSRF cookie must be readable by the SPA. Every other cookie here is HttpOnly.
    public CookieOptions DeleteOptions(CookieOptions like) => new()
    {
        HttpOnly = like.HttpOnly,
        Secure = like.Secure,
        SameSite = like.SameSite,
        Path = "/",
        IsEssential = true,
    };

    private string Prefixed(string name) => Secure ? "__Host-" + name : name;

    private CookieOptions Build(SameSiteMode sameSite, TimeSpan maxAge, bool httpOnly = true) => new()
    {
        HttpOnly = httpOnly,
        Secure = Secure,
        SameSite = sameSite,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
#pragma warning restore S3330
#pragma warning restore S2092
}
