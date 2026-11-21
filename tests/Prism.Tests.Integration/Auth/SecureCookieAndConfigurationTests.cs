using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prism.Api;
using Prism.Api.Auth;

namespace Prism.Tests.Integration.Auth;

public sealed class SecureCookieAndConfigurationTests
{
    [Fact]
    public async Task With_secure_cookies_the_handshake_cookies_use_the_host_prefix_and_the_secure_flag()
    {
        using var factory = new AuthTestFactory(secureCookies: true);
        using CookieClient client = factory.NewClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/sso/start");

        string state = CookieClient.SetCookie(response, "__Host-prism_sso_state")!;
        Assert.Contains("secure", state, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", state, StringComparison.OrdinalIgnoreCase);
        string xsrf = CookieClient.SetCookie(response, "XSRF-TOKEN")!;
        Assert.Contains("secure", xsrf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_host_refuses_to_start_without_sign_in_configuration()
    {
        // Built by hand rather than through WebApplicationFactory: when startup fails there, the host is torn down on another
        // thread while the test is still using it, and the exception the test sees is a race.
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPrismAuth(builder.Configuration);
        await using WebApplication app = builder.Build();

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(ex.Failures, failure => failure.Contains("Auth:ApplicationId", StringComparison.Ordinal));
        Assert.Contains(ex.Failures, failure => failure.Contains("SessionSigningKeyBase64", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_host_refuses_to_start_without_a_database_connection_string()
    {
        // Testing, not the default Production: a deployed host would also demand Redis, which this test is not about.
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddPrismPersistence(builder.Configuration);
        await using WebApplication app = builder.Build();

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(ex.Failures, failure => failure.Contains("ConnectionStrings:Prism", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_deployed_host_refuses_to_start_without_redis()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = "Host=unused.invalid;Database=unused" });
        builder.Services.AddPrismPersistence(builder.Configuration);
        await using WebApplication app = builder.Build();

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(ex.Failures, failure => failure.Contains("ConnectionStrings:Redis", StringComparison.Ordinal));
    }
}
