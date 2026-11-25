using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prism.Infrastructure.Persistence;
using Prism.Application.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Auth;

/// <summary>A rotated secret reaches a running host: the session signing key is read per token and per sign-in, never captured at start.</summary>
public sealed class SecretRotationTests
{
    private static readonly string[] Dashboard = [Permissions.App, Permissions.DashboardView];

    private static void Rotate(AuthTestFactory factory, string key)
    {
        // What the hourly refresh does to the configuration: a source changes and the change token fires.
        var configuration = (IConfigurationBuilder)factory.Services.GetRequiredService<IConfiguration>();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:SessionSigningKeyBase64"] = key });
    }

    [Fact]
    public async Task A_rotated_session_key_signs_existing_sessions_out_and_signs_new_ones_in()
    {
        using var factory = new AuthTestFactory();
        using CookieClient before = factory.NewSignedInClientAs(81, "rotator", Dashboard);
        using HttpResponseMessage signedIn = await before.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        Rotate(factory, PortalTestKeys.NewSessionKeyBase64());

        using HttpResponseMessage old = await before.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);

        using CookieClient after = factory.NewSignedInClientAs(81, "rotator", Dashboard);
        using HttpResponseMessage fresh = await after.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task A_rotated_session_key_that_is_not_valid_fails_closed()
    {
        using var factory = new AuthTestFactory();
        using CookieClient client = factory.NewSignedInClientAs(82, "rotator", Dashboard);
        using HttpResponseMessage signedIn = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        // The options validators reject the new value while the change is applied. In the loader that is the logged "reload subscriber" failure
        // (configuration is not rolled back). From then on nothing is accepted, rather than a session being checked against a key that is not the secret.
        Assert.ThrowsAny<Exception>(() => Rotate(factory, "too-short"));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_rotated_database_connection_string_reaches_the_next_context_and_not_the_running_one()
    {
        using var factory = new AuthTestFactory();
        factory.CreateClient().Dispose();
        const string rotated = "Host=rotated.invalid;Database=rotated;Username=u;Password=p";

        using IServiceScope running = factory.Services.CreateScope();
        string before = running.ServiceProvider.GetRequiredService<PrismDbContext>().Database.GetConnectionString()!;
        ((IConfigurationBuilder)factory.Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = rotated });

        using IServiceScope next = factory.Services.CreateScope();
        string after = next.ServiceProvider.GetRequiredService<PrismDbContext>().Database.GetConnectionString()!;

        Assert.Contains("unused.invalid", before, StringComparison.Ordinal);
        Assert.Equal(rotated, after);
        Assert.Contains("unused.invalid", running.ServiceProvider.GetRequiredService<PrismDbContext>().Database.GetConnectionString()!, StringComparison.Ordinal);
    }
}
