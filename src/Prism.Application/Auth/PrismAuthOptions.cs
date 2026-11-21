namespace Prism.Application.Auth;

/// <summary>
/// Sign-in settings. Resource identifiers (URLs, application id) are empty in base configuration on purpose, so a host with
/// no environment file fails startup validation instead of using another environment's portal. Keys come from secrets.
/// </summary>
public sealed class PrismAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The id this app is registered under in the Account Portal, and the expected <c>aud</c> of login tokens.</summary>
    public string ApplicationId { get; set; } = "";

    public string AccountPortalBaseUrl { get; set; } = "";

    public string AdminPortalApiBaseUrl { get; set; } = "";

    /// <summary>The expected <c>iss</c> of login tokens.</summary>
    public string LoginTokenIssuer { get; set; } = "";

    /// <summary>The <c>aud</c> placed in this app's token to the Admin Portal API.</summary>
    public string AdminPortalAudience { get; set; } = "";

    /// <summary>Absolute https URL that serves the portal's login-token public key.</summary>
    public string LoginTokenPublicKeyUrl { get; set; } = "";

    /// <summary>0 means never cache, which is what deployed M6 environments use.</summary>
    public int LoginTokenPublicKeyCacheSeconds { get; set; }

    /// <summary>Secret. PEM private key this app signs its Admin Portal API tokens with.</summary>
    public string ApplicationPrivateKeyPem { get; set; } = "";

    /// <summary>Secret. Base64 key (at least 32 bytes) that signs session cookies. Rotating it ends every session.</summary>
    public string SessionSigningKeyBase64 { get; set; } = "";

    public int SessionLifetimeMinutes { get; set; } = 480;

    /// <summary>
    /// The portal does not echo <c>state</c> today. When true, a callback that has the state cookie from this browser's own
    /// <c>/start</c> but no echoed state is allowed, after the user confirms on a page served by this app.
    /// A callback with no state cookie is always refused, so a link sent by an attacker cannot sign a victim in.
    /// </summary>
    public bool AllowIdpInitiated { get; set; }

    /// <summary>Root-relative path to land on when there is no valid return URL.</summary>
    public string PostLoginRedirectUrl { get; set; } = "/";

    /// <summary>Optional query (for example <c>prompt=login</c>) appended to the portal URL to force re-authentication.</summary>
    public string AccountPortalForceAuthQuery { get; set; } = "";

    /// <summary>
    /// Portal role names may carry an application prefix, as in <c>&lt;prefix&gt;__SuperUser</c>. A reported role matches a Prism
    /// role only when it is exactly that name, or has one of these prefixes (the <see cref="ApplicationId"/> is always accepted).
    /// A role from another application never matches.
    /// </summary>
    public string[] RolePrefixes { get; set; } = [];

    /// <summary>
    /// Longest remaining life a login token may have when it arrives. The replay window is clamped to 15 minutes, so this must
    /// leave room for clock skew: a token that outlives its replay entry could be replayed.
    /// </summary>
    public int MaxLoginTokenLifetimeMinutes { get; set; } = 10;

    /// <summary>The public https origin of this app (for example <c>https://prism.example.com</c>). Used to build the portal logout return URL. Required outside Development.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Per client address, per minute, on the sign-in routes that start a sign-in.</summary>
    public int RateLimitPerMinute { get; set; } = 30;

    /// <summary>Per client address, per minute, on the routes that consume a login token. Lower, because a legitimate user needs one.</summary>
    public int CallbackRateLimitPerMinute { get; set; } = 10;

    /// <summary>Defaults to true outside Development.</summary>
    public bool? RequireSecureCookies { get; set; }
}
