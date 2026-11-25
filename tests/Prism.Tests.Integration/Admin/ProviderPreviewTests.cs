using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Persistence;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Admin;

/// <summary>
/// An Internal User with the customer data viewer tier may open a Provider's aggregate figures by number to lay out a View Template. It is audited once per session and Provider,
/// it never reaches claim-level or Saved View routes, and it needs both the group and the tier. The caller here is assigned to no Provider at all.
/// </summary>
public sealed class ProviderPreviewTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;
    private static readonly string[] Viewer = [Permissions.App, Permissions.DashboardView, Permissions.AdminCustomersView];

    private readonly AuthTestFactory factory;

    public ProviderPreviewTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = postgres.ConnectionString },
            configureServices: services =>
            {
                services.RemoveAll<IProviderDivisionDirectory>();
                services.AddSingleton<IProviderDivisionDirectory>(new Divisions());
            });
        // Nobody in these tests is assigned to any Provider.
        factory.Directory.ProvidersByUser = _ => [];
    }

    public void Dispose() => factory.Dispose();

    private CookieClient Previewer(params string[] permissions)
    {
        CookieClient client = factory.NewSignedInClientInGroups([Group], permissions.Length > 0 ? permissions : Viewer);
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private async Task<int> AuditRowsAsync(int providerId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PrismDbContext>();
        return await db.AdminAuditEntries.CountAsync(e => e.Action == AdminAuditActions.ProviderPreviewed && e.ProviderId == providerId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_viewer_in_the_group_can_open_any_providers_aggregate_routes_and_it_is_audited_once_per_session()
    {
        using CookieClient viewer = Previewer();

        using HttpResponseMessage first = await viewer.GetAsync("/api/v1/providers/4101/metrics/catalog");
        using HttpResponseMessage second = await viewer.GetAsync("/api/v1/providers/4101/metrics/catalog");
        using HttpResponseMessage other = await viewer.GetAsync("/api/v1/providers/4102/metrics/catalog");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(1, await AuditRowsAsync(4101));
        Assert.Equal(1, await AuditRowsAsync(4102));
    }

    [Fact]
    public async Task A_new_session_is_audited_again()
    {
        using CookieClient first = Previewer();
        using CookieClient second = Previewer();

        await first.GetAsync("/api/v1/providers/4201/metrics/catalog");
        await second.GetAsync("/api/v1/providers/4201/metrics/catalog");

        Assert.Equal(2, await AuditRowsAsync(4201));
    }

    [Fact]
    public async Task Without_the_tier_or_without_the_group_the_provider_looks_like_it_is_not_there_and_nothing_is_audited()
    {
        using CookieClient noTier = Previewer(Permissions.App, Permissions.DashboardView, Permissions.AdminTemplatesAuthor);
        using CookieClient outsideGroup = factory.NewSignedInClientInGroups(["Testing group"], Permissions.App, Permissions.DashboardView, Permissions.AdminCustomersView);
        using CookieClient customer = factory.NewSignedInClientAs(7, "customer", Permissions.App, Permissions.DashboardView);

        Assert.True((await noTier.GetAsync("/api/v1/providers/4301/metrics/catalog")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True((await outsideGroup.GetAsync("/api/v1/providers/4301/metrics/catalog")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True((await customer.GetAsync("/api/v1/providers/4301/metrics/catalog")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(0, await AuditRowsAsync(4301));
    }

    [Fact]
    public async Task A_viewer_can_never_reach_claim_level_routes_or_saved_views_of_a_provider_they_are_not_assigned_to()
    {
        using CookieClient viewer = Previewer(Permissions.App, Permissions.AdminCustomersView, Permissions.DashboardView, Permissions.ClaimsView, Permissions.ViewsManage);

        using HttpResponseMessage counts = await viewer.GetAsync("/api/v1/providers/4401/claims/attention/counts?period=202610");
        using HttpResponseMessage claims = await viewer.GetAsync("/api/v1/providers/4401/claims/attention/denied?period=202610&line=air");
        using HttpResponseMessage export = await viewer.GetAsync("/api/v1/providers/4401/claims/attention/denied/export?period=202610&line=air");
        using HttpResponseMessage views = await viewer.GetAsync("/api/v1/providers/4401/saved-views");

        Assert.True(counts.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True(claims.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True(export.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True(views.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(0, await AuditRowsAsync(4401));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("abc")]
    [InlineData("99999999999")]
    [InlineData("1%20")]
    public async Task A_provider_that_is_not_a_plain_positive_number_is_refused(string providerId)
    {
        using CookieClient viewer = Previewer();

        using HttpResponseMessage response = await viewer.GetAsync($"/api/v1/providers/{providerId}/metrics/catalog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_header_or_query_value_cannot_turn_a_customer_into_a_previewer()
    {
        using CookieClient customer = factory.NewSignedInClientAs(7, "customer", Permissions.App, Permissions.DashboardView);

        using HttpResponseMessage response = await customer.SendAsync(
            HttpMethod.Get,
            "/api/v1/providers/4501/metrics/catalog?internal=true",
            null,
            new Dictionary<string, string> { ["X-Internal"] = "true", ["X-Preview-Provider"] = "4501" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await AuditRowsAsync(4501));
    }

    [Fact]
    public async Task A_read_is_refused_when_the_audit_cannot_be_written()
    {
        await using AuthTestFactory broken = new(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = "Host=unused.invalid;Database=unused" },
            configureServices: services =>
            {
                services.RemoveAll<IProviderDivisionDirectory>();
                services.AddSingleton<IProviderDivisionDirectory>(new Divisions());
                services.RemoveAll<IPreviewAudit>();
                services.AddSingleton<IPreviewAudit>(new FailingAudit());
            });
        broken.Directory.ProvidersByUser = _ => [];
        using CookieClient viewer = broken.NewSignedInClientInGroups([Group], Viewer);

        using HttpResponseMessage response = await viewer.GetAsync("/api/v1/providers/4601/metrics/catalog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FailingAudit : IPreviewAudit
    {
        public Task<bool> RecordAsync(AdminActor actor, int providerId, string sessionId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Divisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingDivision>>([]);
    }
}
