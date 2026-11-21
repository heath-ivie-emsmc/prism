using System.Net;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Auth;

/// <summary>The portal does not echo state today, so the confirmation page is the path real logins take.</summary>
public sealed class IdpInitiatedSignInTests : IDisposable
{
    private readonly AuthTestFactory factory = new(allowIdpInitiated: true);
    private readonly CookieClient client;

    public IdpInitiatedSignInTests() => client = factory.NewClient();

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private string LoginToken(string tokenId = "nonce-1") =>
        factory.Portal.Mint(DateTimeOffset.UtcNow, new LoginTokenSpec { TokenId = tokenId });

    // A real user starts at /start (which sets the state cookie), and the portal then comes back without echoing state.
    private async Task<HttpResponseMessage> CallbackWithoutStateAsync(string token)
    {
        using HttpResponseMessage start = await client.GetAsync("/api/auth/sso/start");
        return await client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(token)}");
    }

    private static FormUrlEncodedContent Form(string loginToken, string nonce) =>
        new(new Dictionary<string, string> { ["loginToken"] = loginToken, ["nonce"] = nonce });

    [Fact]
    public async Task A_callback_without_state_shows_a_confirmation_page_and_does_not_sign_in()
    {
        using HttpResponseMessage response = await CallbackWithoutStateAsync(LoginToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
        Assert.Empty(factory.Directory.Nonces);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("action=\"/api/auth/sso/confirm\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"nonce\"", html, StringComparison.Ordinal);
        Assert.NotNull(CookieClient.SetCookie(response, "prism_sso_confirm"));
    }

    [Fact]
    public async Task The_confirmation_page_cannot_be_framed_cached_or_used_to_run_script()
    {
        using HttpResponseMessage response = await CallbackWithoutStateAsync(LoginToken());

        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        string csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submitting_the_confirmation_with_the_matching_nonce_signs_the_user_in()
    {
        string token = LoginToken();
        using HttpResponseMessage page = await CallbackWithoutStateAsync(token);
        string nonce = client.Cookies["prism_sso_confirm"];

        using HttpResponseMessage response = await client.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, nonce));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.NotNull(CookieClient.SetCookie(response, "prism_session"));
        Assert.Single(factory.Directory.Nonces);
    }

    [Fact]
    public async Task Confirmation_from_another_browser_is_refused()
    {
        string token = LoginToken();
        using HttpResponseMessage page = await CallbackWithoutStateAsync(token);
        string nonce = client.Cookies["prism_sso_confirm"];
        using var attacker = factory.NewClient();

        using HttpResponseMessage response = await attacker.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, nonce));

        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_wrong_nonce_is_refused_and_spends_the_confirm_cookie()
    {
        string token = LoginToken();
        using HttpResponseMessage page = await CallbackWithoutStateAsync(token);
        string nonce = client.Cookies["prism_sso_confirm"];

        using HttpResponseMessage wrong = await client.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, "0000"));
        using HttpResponseMessage retry = await client.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, nonce));

        Assert.Equal("/login?error=sso_failed", wrong.Headers.Location!.OriginalString);
        Assert.Equal("/login?error=sso_failed", retry.Headers.Location!.OriginalString);
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_confirmation_is_not_accepted_twice()
    {
        string token = LoginToken();
        using HttpResponseMessage page = await CallbackWithoutStateAsync(token);
        string nonce = client.Cookies["prism_sso_confirm"];
        using HttpResponseMessage first = await client.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, nonce));
        client.Set("prism_sso_confirm", nonce);

        using HttpResponseMessage second = await client.SendAsync(HttpMethod.Post, "/api/auth/sso/confirm", Form(token, nonce));

        Assert.Equal("/", first.Headers.Location!.OriginalString);
        Assert.Equal("/login?error=sso_failed", second.Headers.Location!.OriginalString);
        Assert.Single(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_callback_link_sent_to_a_browser_that_never_started_sign_in_is_refused()
    {
        // Login CSRF: the attacker's own valid token, delivered as a link. The victim has no state cookie from /start.
        using HttpResponseMessage response = await client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(LoginToken())}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_sso_confirm"));
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task The_state_cookie_is_spent_by_the_first_callback()
    {
        using HttpResponseMessage first = await CallbackWithoutStateAsync(LoginToken());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage second = await client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(LoginToken("nonce-2"))}");

        Assert.Equal("/login?error=sso_failed", second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_login_token_is_html_encoded_on_the_confirmation_page()
    {
        using HttpResponseMessage response = await CallbackWithoutStateAsync("\"><img src=x onerror=alert(1)>");

        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }
}

public sealed class IdpInitiatedDisabledTests : IDisposable
{
    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task The_confirm_route_does_not_exist_when_idp_initiated_logins_are_off()
    {
        using var client = factory.NewClient();

        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Post, "/api/auth/sso/confirm",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["loginToken"] = "x", ["nonce"] = "y" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
