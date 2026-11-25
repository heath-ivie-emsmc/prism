using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration;

public sealed class SpaHostingTests : IDisposable
{
    private readonly string webRoot = Directory.CreateTempSubdirectory("prism-wwwroot-").FullName;
    private readonly AuthTestFactory factory = new();
    private readonly WebApplicationFactory<Program> hosted;

    public SpaHostingTests()
    {
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>spa-shell</title>");
        File.WriteAllText(Path.Combine(webRoot, "main-abc.js"), "console.log('app');");
        hosted = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
    }

    public void Dispose()
    {
        hosted.Dispose();
        factory.Dispose();
        Directory.Delete(webRoot, recursive: true);
    }

    private HttpClient Client() => hosted.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/dashboard")]
    [InlineData("/dashboard/some/deep/link")]
    public async Task Client_side_routes_are_served_the_spa_shell(string path)
    {
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("spa-shell", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_assets_are_served_as_they_are()
    {
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.GetAsync("/main-abc.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("console.log", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api")]
    public async Task Unknown_api_paths_are_never_answered_with_html(string path)
    {
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("spa-shell", body, StringComparison.Ordinal);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_api_contract_for_me_matches_what_the_spa_reads()
    {
        // The SPA's MeDto is { userName, displayName, providers: [{ id, name, facilityCount, permissions }], permissions, isInternal, adminPermissions, onboardingRequired }.
        using var portalClient = factory.NewClient();
        using HttpResponseMessage start = await portalClient.GetAsync("/api/auth/sso/start");
        string state = System.Web.HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
        string token = factory.Portal.Mint(DateTimeOffset.UtcNow);
        using HttpResponseMessage callback = await portalClient.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(token)}&state={state}");
        using HttpResponseMessage me = await portalClient.GetAsync("/api/v1/me");

        using var json = System.Text.Json.JsonDocument.Parse(await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        System.Text.Json.JsonElement root = json.RootElement;

        Assert.Equal(["adminPermissions", "displayName", "isInternal", "onboardingRequired", "permissions", "providers", "userName"], root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        System.Text.Json.JsonElement provider = root.GetProperty("providers")[0];
        Assert.Equal(["facilityCount", "id", "name", "permissions"], provider.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }
}
