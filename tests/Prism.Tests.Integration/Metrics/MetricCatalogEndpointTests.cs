using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The catalog route: definitions only, for a Provider the caller may read in full, with the dashboard permission.</summary>
public sealed class MetricCatalogEndpointTests : IDisposable
{
    private readonly AuthTestFactory factory;

    public MetricCatalogEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new Divisions());
        });
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    private async Task<(HttpStatusCode Status, string Text)> GetAsync(string url, params string[] permissions)
    {
        using CookieClient client = factory.NewSignedInClient(permissions);
        using HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_user_with_access_gets_categories_and_metrics_with_status_and_presentations()
    {
        (HttpStatusCode status, string text) = await GetAsync("/api/v1/providers/100/metrics/catalog");

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement body = JsonElement.Parse(text);
        Assert.NotEmpty(body.GetProperty("categories").EnumerateArray());
        JsonElement netCash = body.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("key").GetString() == "net_collections");
        Assert.Equal("available", netCash.GetProperty("status").GetString());
        Assert.True(netCash.GetProperty("placeable").GetBoolean());
        Assert.Contains(netCash.GetProperty("presentations").EnumerateArray(), p => p.GetProperty("key").GetString() == "line" && p.GetProperty("isDefault").GetBoolean());
        JsonElement planned = body.GetProperty("metrics").EnumerateArray().First(m => m.GetProperty("status").GetString() == "planned");
        Assert.False(planned.GetProperty("placeable").GetBoolean());
    }

    [Fact]
    public async Task The_catalog_carries_definitions_only_and_no_provider_or_patient_values()
    {
        (_, string text) = await GetAsync("/api/v1/providers/100/metrics/catalog");

        Assert.DoesNotContain("Mercy Air", text, StringComparison.Ordinal);
        Assert.DoesNotContain("providerId", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/catalog")]
    [InlineData("/api/v1/providers/200/metrics/catalog")]
    [InlineData("/api/v1/providers/abc/metrics/catalog")]
    public async Task A_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task The_catalog_needs_the_dashboard_permission_and_a_signed_in_user()
    {
        (HttpStatusCode forbidden, _) = await GetAsync("/api/v1/providers/100/metrics/catalog", Permissions.App);
        using CookieClient anonymous = factory.NewClient();
        using HttpResponseMessage response = await anonymous.GetAsync("/api/v1/providers/100/metrics/catalog");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class Divisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId == 200 ? new HashSet<string> { "9", "10" } : []);

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingDivision>>([]);
    }
}
