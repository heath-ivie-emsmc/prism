using System.Net;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Auth;

[Collection(Prism.Tests.Integration.LogCaptureGroup.Name)]
public sealed class SignInTests : IDisposable
{
    private static readonly string[] ForgedPermissions = ["prism.app", "prism.dashboard.view"];

    private readonly AuthTestFactory factory = new();
    private readonly CookieClient client;

    public SignInTests() => client = factory.NewClient();

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private string LoginToken(LoginTokenSpec? spec = null) => factory.Portal.Mint(DateTimeOffset.UtcNow, spec);

    private async Task<string> StartAndGetStateAsync(string query = "")
    {
        using HttpResponseMessage start = await client.GetAsync("/api/auth/sso/start" + query);
        var location = start.Headers.Location!;
        return System.Web.HttpUtility.ParseQueryString(location.Query)["state"]!;
    }

    private Task<HttpResponseMessage> CallbackAsync(string token, string? state) =>
        client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(token)}" + (state is null ? "" : $"&state={Uri.EscapeDataString(state)}"));

    [Fact]
    public async Task With_a_local_login_return_url_start_asks_the_portal_to_send_the_token_there()
    {
        using var local = new AuthTestFactory(settings: new Dictionary<string, string?> { ["Auth:LoginReturnUrl"] = "http://localhost:4200/api/auth/sso/callback" });
        using CookieClient localClient = local.NewClient();

        using HttpResponseMessage response = await localClient.GetAsync("/api/auth/sso/start");

        var location = response.Headers.Location!;
        Assert.Equal("/login", location.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal(PortalTestKeys.ApplicationId, query["appId"]);
        Assert.Equal("http://localhost:4200/api/auth/sso/callback", query["returnUrl"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public async Task Without_one_start_sends_no_return_url_to_the_portal()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/start");

        Assert.DoesNotContain("returnUrl", response.Headers.Location!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_redirects_to_the_portal_with_the_application_id_and_a_fresh_state()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/start");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(AuthTestFactory.PortalBase + "/", location.ToString(), StringComparison.Ordinal);
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal(PortalTestKeys.ApplicationId, query["appId"]);
        Assert.Equal(64, query["state"]!.Length);

        string stateCookie = CookieClient.SetCookie(response, "prism_sso_state")!;
        Assert.Equal(query["state"], CookieClient.Value(stateCookie));
        Assert.Contains("httponly", stateCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", stateCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_issues_a_different_state_each_time()
    {
        string first = await StartAndGetStateAsync();
        string second = await StartAndGetStateAsync();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Start_remembers_only_a_safe_return_url()
    {
        using HttpResponseMessage safe = await client.GetAsync("/api/auth/sso/start?returnUrl=%2Fdashboard");
        using HttpResponseMessage evil = await client.GetAsync("/api/auth/sso/start?returnUrl=%2F%2Fevil.com");

        Assert.NotNull(CookieClient.SetCookie(safe, "prism_sso_return"));
        // The unsafe target is not stored, and it also clears the earlier one instead of letting it carry over.
        string? cleared = CookieClient.SetCookie(evil, "prism_sso_return");
        Assert.True(cleared is null || CookieClient.Value(cleared).Length == 0);
    }

    [Fact]
    public async Task A_valid_callback_signs_the_user_in_and_goes_to_the_remembered_page()
    {
        string state = await StartAndGetStateAsync("?returnUrl=%2Fdashboard%3Fperiod%3D2026-10");

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), state);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dashboard?period=2026-10", response.Headers.Location!.OriginalString);
        string session = CookieClient.SetCookie(response, "prism_session")!;
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=Thu, 01 Jan 1970", session, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["nonce-1"], factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_user_holding_the_internal_marker_role_is_internal_and_gets_no_providers()
    {
        // The portal reports no group names; its group carries a role, and every company, so Providers must be ignored for this user.
        factory.Directory.Handler = _ => new PortalUser("jdoe", "Jane Doe", 42, ["InternalUser"], []);
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        using HttpResponseMessage me = await client.GetAsync("/api/v1/me");

        string body = await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"isInternal\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"providers\":[]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_sign_in_me_returns_the_user_providers_and_permissions()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);

        using HttpResponseMessage me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        string body = await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"userName\":\"jdoe\"", body, StringComparison.Ordinal);
        Assert.Contains("\"displayName\":\"Jane Doe\"", body, StringComparison.Ordinal);
        Assert.Contains("\"permissions\":[\"prism.app\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"providers\":[{\"id\":\"100\",\"name\":\"Mercy Air\",\"facilityCount\":2,\"permissions\":[", body, StringComparison.Ordinal);
        Assert.DoesNotContain("facilityIds", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_state_is_single_use()
    {
        string state = await StartAndGetStateAsync();
        string stateCookie = client.Cookies["prism_sso_state"];
        using HttpResponseMessage first = await CallbackAsync(LoginToken(new LoginTokenSpec { TokenId = "a" }), state);
        Assert.Equal("/", first.Headers.Location!.OriginalString);

        // Replaying the old state cookie with a new token: the cookie was deleted, so there is nothing to match.
        client.Set("prism_sso_state", stateCookie);
        using HttpResponseMessage second = await CallbackAsync(LoginToken(new LoginTokenSpec { TokenId = "b" }), "different");

        Assert.Equal("/login?error=sso_failed", second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_state_that_does_not_match_is_refused_and_creates_no_session()
    {
        await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), "forged-state");

        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_callback_without_state_is_refused_when_idp_initiated_logins_are_off()
    {
        using HttpResponseMessage response = await CallbackAsync(LoginToken(), null);

        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_replayed_login_token_is_refused()
    {
        string token = LoginToken();
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage first = await CallbackAsync(token, state);
        Assert.Equal("/", first.Headers.Location!.OriginalString);

        string secondState = await StartAndGetStateAsync();
        using HttpResponseMessage second = await CallbackAsync(token, secondState);

        Assert.Equal("/login?error=sso_failed", second.Headers.Location!.OriginalString);
        Assert.Single(factory.Directory.Nonces);
    }

    [Fact]
    public async Task An_expired_token_reports_that_the_sign_in_took_too_long()
    {
        string state = await StartAndGetStateAsync();
        string token = factory.Portal.Mint(DateTimeOffset.UtcNow.AddMinutes(-10), new LoginTokenSpec { ExpiresIn = TimeSpan.FromMinutes(1) });

        using HttpResponseMessage response = await CallbackAsync(token, state);

        Assert.Equal("/login?error=sso_expired", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Directory.Nonces);
    }

    [Fact]
    public async Task A_token_signed_by_someone_else_is_refused()
    {
        using var impostor = new PortalTestKeys();
        string state = await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(impostor.Mint(DateTimeOffset.UtcNow), state);

        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
    }

    [Fact]
    public async Task A_user_with_no_role_for_this_app_is_denied()
    {
        factory.Directory.Handler = _ => new PortalUser("jdoe", "Jane Doe", 42, [], []);
        string state = await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), state);

        Assert.Equal("/login?error=access_denied", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
    }

    [Fact]
    public async Task A_user_whose_roles_are_not_prism_roles_is_denied()
    {
        factory.Directory.Handler = _ => new PortalUser("jdoe", "Jane Doe", 42, ["SomeOtherAppRole"], []);
        string state = await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), state);

        Assert.Equal("/login?error=access_denied", response.Headers.Location!.OriginalString);
        Assert.Null(CookieClient.SetCookie(response, "prism_session"));
    }

    [Fact]
    public async Task A_portal_failure_is_reported_generically_without_leaking_the_cause()
    {
        factory.Directory.Handler = _ => throw new HttpRequestException("secret internal detail at https://internal.example");
        string state = await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), state);

        Assert.Equal("/login?error=sso_failed", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("secret", response.Headers.Location.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_with_thousands_of_facilities_still_signs_in_because_scope_is_not_in_the_cookie()
    {
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange(Enumerable.Range(0, 400).Select(i =>
            new ProviderScope($"{i}", $"Provider {i}", Enumerable.Range(0, 5000).Select(f => $"{f}").ToList())));
        string state = await StartAndGetStateAsync();

        using HttpResponseMessage response = await CallbackAsync(LoginToken(), state);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
        string cookie = CookieClient.SetCookie(response, "prism_session")!;
        Assert.True(cookie.Length < 4000, $"session cookie was {cookie.Length} characters");
    }

    [Fact]
    public async Task Me_reads_the_scope_from_the_portal_and_remembers_it_between_requests()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);

        using HttpResponseMessage first = await client.GetAsync("/api/v1/me");
        using HttpResponseMessage second = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, factory.Directory.ProviderLookups);
    }

    [Fact]
    public async Task Me_with_an_unreachable_portal_returns_no_providers_instead_of_failing_the_page()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        factory.Directory.FailProviders = true;

        using HttpResponseMessage me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains("\"providers\":[]", await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_names_and_tokens_from_a_rejected_sign_in_stay_out_of_the_log_at_normal_levels()
    {
        const string marker = "PHI-MARKER-4471";
        factory.Directory.Handler = _ => new PortalUser("jdoe", "Jane Doe", 42, [$"{marker}-role"], []);
        string state = await StartAndGetStateAsync();
        string token = LoginToken();

        using HttpResponseMessage response = await CallbackAsync(token, state);

        Assert.Equal("/login?error=access_denied", response.Headers.Location!.OriginalString);
        var visible = factory.Logs.Entries.Where(entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Information).ToList();
        Assert.Contains(visible, entry => entry.Text.Contains("no role", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(visible, entry => entry.Text.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(visible, entry => entry.Text.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Me_without_a_session_is_unauthorized()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_session_cookie_signed_with_another_key_is_not_accepted()
    {
        var forged = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "prism-api",
            Audience = "prism-web",
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = "attacker", ["permissions"] = ForgedPermissions },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256),
        });
        client.Set("prism_session", forged);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_session_sent_as_a_bearer_header_is_ignored()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        string session = client.Cookies["prism_session"];
        using var bare = factory.NewClient();

        using HttpResponseMessage response = await bare.SendAsync(
            HttpMethod.Get, "/api/v1/me", headers: new Dictionary<string, string> { ["Authorization"] = "Bearer " + session });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session_and_sends_the_user_to_the_portal_logout()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        Assert.True(client.Cookies.ContainsKey("prism_session"));

        using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(AuthTestFactory.PortalBase + "/logout?appId=" + PortalTestKeys.ApplicationId, response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(client.Cookies.ContainsKey("prism_session"));
        using HttpResponseMessage me = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_so_a_copied_cookie_stops_working()
    {
        string state = await StartAndGetStateAsync();
        using HttpResponseMessage callback = await CallbackAsync(LoginToken(), state);
        string copiedSession = client.Cookies["prism_session"];

        using HttpResponseMessage logout = await client.GetAsync("/api/auth/sso/logout");

        Assert.Single(factory.Revocations.Revoked);
        Assert.Equal("jdoe", factory.Revocations.Revoked.Values.Single());
        using var attacker = factory.NewClient();
        attacker.Set("prism_session", copiedSession);
        using HttpResponseMessage replay = await attacker.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Revoking_one_session_leaves_the_same_users_other_session_alone()
    {
        using HttpResponseMessage first = await CallbackAsync(LoginToken(), await StartAndGetStateAsync());
        string firstSession = client.Cookies["prism_session"];
        using HttpResponseMessage second = await CallbackAsync(
            LoginToken(new LoginTokenSpec { TokenId = "nonce-2" }), await StartAndGetStateAsync());
        Assert.Equal("/", second.Headers.Location!.OriginalString);
        Assert.NotEqual(firstSession, client.Cookies["prism_session"]);

        // Logs out the second session only: the client now carries it.
        using HttpResponseMessage logout = await client.GetAsync("/api/auth/sso/logout");

        using var firstBrowser = factory.NewClient();
        firstBrowser.Set("prism_session", firstSession);
        using HttpResponseMessage me = await firstBrowser.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Many_users_can_sign_in_one_after_another()
    {
        // Regression: a cached signature provider held a disposed key, so every sign-in after the first failed.
        for (int i = 0; i < 3; i++)
        {
            using HttpResponseMessage response = await CallbackAsync(
                LoginToken(new LoginTokenSpec { TokenId = $"nonce-seq-{i}" }), await StartAndGetStateAsync());

            Assert.Equal("/", response.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task Logout_without_a_session_revokes_nothing()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(factory.Revocations.Revoked);
    }

    [Fact]
    public async Task State_changing_requests_need_the_xsrf_header()
    {
        using HttpResponseMessage first = await client.GetAsync("/api/v1/me");
        // Cookie values are URL-encoded on the wire. The SPA decodes before echoing the token, so the test does too.
        string xsrf = Uri.UnescapeDataString(client.Cookies["XSRF-TOKEN"]);

        using HttpResponseMessage without = await client.SendAsync(HttpMethod.Post, "/api/anything", new StringContent("{}"));
        using HttpResponseMessage wrong = await client.SendAsync(
            HttpMethod.Post, "/api/anything", new StringContent("{}"), new Dictionary<string, string> { ["X-XSRF-TOKEN"] = "not-it" });
        using HttpResponseMessage matching = await client.SendAsync(
            HttpMethod.Post, "/api/anything", new StringContent("{}"), new Dictionary<string, string> { ["X-XSRF-TOKEN"] = xsrf });

        Assert.Equal(HttpStatusCode.Forbidden, without.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, matching.StatusCode);
    }

    [Fact]
    public async Task The_xsrf_cookie_is_readable_by_scripts_and_the_session_cookie_is_not()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");

        string xsrf = CookieClient.SetCookie(response, "XSRF-TOKEN")!;
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_in_routes_are_rate_limited_per_address()
    {
        HttpStatusCode last = HttpStatusCode.OK;
        for (int i = 0; i < 40 && last != HttpStatusCode.TooManyRequests; i++)
        {
            using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/start");
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task An_unauthorized_response_does_not_leak_server_details()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }
}
