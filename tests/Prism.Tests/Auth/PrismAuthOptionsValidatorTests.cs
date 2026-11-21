using Microsoft.Extensions.Options;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

public sealed class PrismAuthOptionsValidatorTests
{
    private static PrismAuthOptions Valid() => new()
    {
        ApplicationId = "prism-test",
        AccountPortalBaseUrl = "https://account.example.test",
        AdminPortalApiBaseUrl = "https://api.example.test",
        LoginTokenIssuer = "account-portal-test",
        AdminPortalAudience = "admin-portal-api-test",
        LoginTokenPublicKeyUrl = "https://account.example.test/key/public",
        ApplicationPrivateKeyPem = "pem",
        SessionSigningKeyBase64 = PortalTestKeys.NewSessionKeyBase64(),
    };

    private static ValidateOptionsResult Validate(PrismAuthOptions options) =>
        new PrismAuthOptionsValidator().Validate(null, options);

    [Fact]
    public void Accepts_a_complete_configuration()
    {
        Assert.True(Validate(Valid()).Succeeded);
    }

    [Fact]
    public void Accepts_loopback_http_for_local_development()
    {
        PrismAuthOptions options = Valid();
        options.AdminPortalApiBaseUrl = "http://localhost:5001";

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Accepts_a_loopback_login_return_url_for_local_development()
    {
        PrismAuthOptions options = Valid();
        options.LoginReturnUrl = "http://localhost:4200/api/auth/sso/callback";

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData("https://prism.example.test/api/auth/sso/callback")]
    [InlineData("http://evil.example.test/api/auth/sso/callback")]
    [InlineData("http://user:pw@localhost:4200/cb")]
    [InlineData("/api/auth/sso/callback")]
    public void Rejects_a_login_return_url_that_is_not_plain_loopback(string url)
    {
        PrismAuthOptions options = Valid();
        options.LoginReturnUrl = url;

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("LoginReturnUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_empty_base_configuration()
    {
        ValidateOptionsResult result = Validate(new PrismAuthOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("ApplicationId", StringComparison.Ordinal));
        Assert.Contains(result.Failures, f => f.Contains("SessionSigningKeyBase64", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://account.example.test")]
    [InlineData("ftp://account.example.test")]
    [InlineData("not a url")]
    public void Rejects_portal_urls_that_are_not_https(string url)
    {
        PrismAuthOptions options = Valid();
        options.AccountPortalBaseUrl = url;

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!!")]
    [InlineData("c2hvcnQ=")]
    public void Rejects_a_weak_or_malformed_session_key(string key)
    {
        PrismAuthOptions options = Valid();
        options.SessionSigningKeyBase64 = key;

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void Rejects_an_out_of_range_session_lifetime(int minutes)
    {
        PrismAuthOptions options = Valid();
        options.SessionLifetimeMinutes = minutes;

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("/\\evil")]
    public void Rejects_a_post_login_redirect_that_is_not_a_safe_relative_path(string path)
    {
        PrismAuthOptions options = Valid();
        options.PostLoginRedirectUrl = path;

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Rejects_a_login_token_lifetime_the_replay_window_cannot_cover(int minutes)
    {
        PrismAuthOptions options = Valid();
        options.MaxLoginTokenLifetimeMinutes = minutes;

        Assert.False(Validate(options).Succeeded);
    }

    [Fact]
    public void Rejects_rate_limits_below_one()
    {
        PrismAuthOptions options = Valid();
        options.CallbackRateLimitPerMinute = 0;

        Assert.False(Validate(options).Succeeded);
    }

    [Fact]
    public void Rejects_a_plain_http_public_origin_but_accepts_https_and_loopback()
    {
        PrismAuthOptions options = Valid();
        options.PublicBaseUrl = "http://prism.example.test";
        Assert.False(Validate(options).Succeeded);

        options.PublicBaseUrl = "https://prism.example.test";
        Assert.True(Validate(options).Succeeded);

        options.PublicBaseUrl = "http://localhost:4200";
        Assert.True(Validate(options).Succeeded);
    }
}
