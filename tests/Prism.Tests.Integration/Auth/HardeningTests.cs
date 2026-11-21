using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Prism.Api;
using Prism.Api.Auth;
using Prism.Application.Auth;

namespace Prism.Tests.Integration.Auth;

public sealed class HardeningTests
{
    [Fact]
    public async Task Every_response_carries_the_baseline_security_headers()
    {
        using var factory = new AuthTestFactory();
        using CookieClient client = factory.NewClient();

        using HttpResponseMessage response = await client.GetAsync("/up");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        string csp = response.Headers.GetValues("Content-Security-Policy-Report-Only").Single();
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_csrf_header_is_a_problem_details_403()
    {
        using var factory = new AuthTestFactory();
        using CookieClient client = factory.NewClient();
        using HttpResponseMessage first = await client.GetAsync("/api/v1/me");

        using HttpResponseMessage response = await client.SendAsync(HttpMethod.Post, "/api/anything", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Staging", true)]
    [InlineData("QA", true)]
    [InlineData("Production", true)]
    public void Every_environment_but_development_and_testing_is_deployed(string name, bool deployed)
    {
        Assert.Equal(deployed, new FakeEnvironment(name).IsDeployedEnvironment());
    }

    [Fact]
    public void Deployed_environments_reject_http_urls_and_insecure_cookies_and_a_missing_public_origin()
    {
        var options = new PrismAuthOptions
        {
            AccountPortalBaseUrl = "http://account.example.test",
            AdminPortalApiBaseUrl = "https://api.example.test",
            LoginTokenPublicKeyUrl = "https://account.example.test/key/public",
            PublicBaseUrl = "",
            RequireSecureCookies = false,
        };

        ValidateOptionsResult result = new DeployedAuthOptionsValidator(new FakeEnvironment("Production")).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("AccountPortalBaseUrl", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("PublicBaseUrl", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("RequireSecureCookies", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployed_environments_accept_a_fully_https_configuration()
    {
        var options = new PrismAuthOptions
        {
            AccountPortalBaseUrl = "https://account.example.test",
            AdminPortalApiBaseUrl = "https://api.example.test",
            LoginTokenPublicKeyUrl = "https://account.example.test/key/public",
            PublicBaseUrl = "https://prism.example.test",
        };

        Assert.True(new DeployedAuthOptionsValidator(new FakeEnvironment("Staging")).Validate(null, options).Succeeded);
    }

    [Fact]
    public void Development_may_use_http_loopback_urls()
    {
        var options = new PrismAuthOptions { AccountPortalBaseUrl = "http://localhost:1" };

        Assert.True(new DeployedAuthOptionsValidator(new FakeEnvironment("Development")).Validate(null, options).Succeeded);
    }

    internal sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Prism.Tests";

        public string ContentRootPath { get; set; } = "/";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
