using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Warehouse;
using Prism.Application.Auth;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Admin;

/// <summary>
/// The admin View Template routes through the real host and real PostgreSQL: who may call them, the whole push journey, and what bad input does.
/// Customers: 31 and 32 are in Provider 100, 33 only in Provider 300, and 34 is limited to one Facility of Provider 100.
/// </summary>
public sealed class ViewTemplateEndpointTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;
    private const string Marker = "PHI-MARKER-Jane-Doe-1970-01-01";
    private const string Templates = "/api/v1/admin/view-templates";
    private const string Views = "/api/v1/providers/100/saved-views";
    private static readonly string[] AuthorOnly = [Permissions.App, Permissions.AdminTemplatesAuthor];
    private static readonly string[] PublisherOnly = [Permissions.App, Permissions.AdminTemplatesPublish];
    private static readonly string[] CustomerPermissions = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];

    private readonly AuthTestFactory factory;
    private int counter;

    public ViewTemplateEndpointTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = postgres.ConnectionString },
            configureServices: services =>
            {
                // The Facility check asks the warehouse which Facilities a Provider has. There is no warehouse here, so a fixed answer stands in.
                services.RemoveAll<IProviderDivisionDirectory>();
                services.AddSingleton<IProviderDivisionDirectory>(new Divisions());
            });
        factory.Directory.ProvidersByUser = user => user switch
        {
            33 => [new ProviderScope("300", "Elsewhere", [])],
            34 => [new ProviderScope("100", "Mercy Air", ["9"])],
            _ => [new ProviderScope("100", "Mercy Air", [])],
        };
    }

    public void Dispose() => factory.Dispose();

    private string Unique() => $"tpl-{Interlocked.Increment(ref counter)}-{Guid.NewGuid():N}"[..30];

    private static CookieClient WithXsrf(CookieClient client)
    {
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private CookieClient AdminClient(params string[] permissions) => WithXsrf(factory.NewSignedInClientInGroups([Group], permissions));

    private CookieClient Customer(long user) => WithXsrf(factory.NewSignedInClientAs(user, $"user{user}", CustomerPermissions));

    private static object Layout(int widgets = 2, string metric = "net_collections") => new
    {
        version = 1,
        density = "comfortable",
        widgets = Enumerable.Range(0, widgets).Select(i => new { id = $"w{i}", metricKey = metric, presentation = "line", x = 0, y = i * 3, w = 6, h = 3 }),
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text, HttpResponseMessage Response)> CallAsync(
        CookieClient client, HttpMethod method, string url, object? body = null, string? ifMatch = null)
    {
        Dictionary<string, string> headers = new() { ["X-XSRF-TOKEN"] = "test-xsrf" };
        if (ifMatch is not null)
        {
            headers["If-Match"] = ifMatch;
        }

        HttpContent? content = body is null ? null : new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.SendAsync(method, url, content, headers);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.StartsWith('{') || text.StartsWith('[') ? JsonElement.Parse(text) : default, text, response);
    }

    private async Task<(int Id, string Name, string ETag)> CreateTemplateAsync(CookieClient admin, int widgets = 2)
    {
        string name = Unique();
        var (status, body, text, response) = await CallAsync(admin, HttpMethod.Post, Templates, new { name, description = "Starter", layout = Layout(widgets) });
        Assert.True(status == HttpStatusCode.Created, text);
        return (body.GetProperty("id").GetInt32(), name, response.Headers.ETag!.Tag);
    }

    private static async Task<int> PushToProviderAsync(CookieClient publisher, int templateId, int providerId, int widgets)
    {
        var (status, body, text, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{templateId}/assignments",
            new { scope = new { kind = "Provider", providerId }, confirmedWidgetCount = widgets });
        Assert.True(status == HttpStatusCode.Created, text);
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<string[]> ViewNamesAsync(CookieClient customer)
    {
        var (status, body, text, _) = await CallAsync(customer, HttpMethod.Get, Views);
        Assert.True(status == HttpStatusCode.OK, text);
        return [.. body.GetProperty("mine").EnumerateArray().Select(v => v.GetProperty("name").GetString()!)];
    }

    public static TheoryData<string, string, string, string> Routes => new()
    {
        { "GET", Templates, "author", "" },
        { "GET", $"{Templates}/1", "author", "" },
        { "POST", Templates, "author", "{}" },
        { "PUT", $"{Templates}/1", "author", "{}" },
        { "DELETE", $"{Templates}/1", "author", "" },
        { "POST", $"{Templates}/1/publish", "publisher", "" },
        { "POST", $"{Templates}/1/unpublish", "publisher", "" },
        { "POST", $"{Templates}/1/assignments/preview", "publisher", "{}" },
        { "GET", $"{Templates}/1/assignments", "publisher", "" },
        { "POST", $"{Templates}/1/assignments", "publisher", "{}" },
        { "DELETE", "/api/v1/admin/template-assignments/1", "publisher", "" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_route_needs_the_group_and_its_own_tier(string method, string url, string tier, string body)
    {
        string[] right = tier == "author" ? AuthorOnly : PublisherOnly;
        string[] wrong = tier == "author" ? PublisherOnly : AuthorOnly;
        object? payload = body.Length == 0 ? null : body;

        using CookieClient anonymous = WithXsrf(factory.NewClient());
        using CookieClient outsideGroup = WithXsrf(factory.NewSignedInClientInGroups(["Testing group"], right));
        using CookieClient noTier = AdminClient(Permissions.App);
        using CookieClient wrongTier = AdminClient(wrong);
        using CookieClient customer = Customer(31);
        using CookieClient allowed = AdminClient(right);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(anonymous, new HttpMethod(method), url, payload)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(outsideGroup, new HttpMethod(method), url, payload)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(noTier, new HttpMethod(method), url, payload)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(wrongTier, new HttpMethod(method), url, payload)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(customer, new HttpMethod(method), url, payload)).Status);
        HttpStatusCode permitted = (await CallAsync(allowed, new HttpMethod(method), url, payload, method == "PUT" ? "\"1\"" : null)).Status;
        Assert.NotEqual(HttpStatusCode.Unauthorized, permitted);
        Assert.NotEqual(HttpStatusCode.Forbidden, permitted);
    }

    [Fact]
    public async Task A_template_goes_from_draft_to_published_to_pushed_and_arrives_as_a_copy_for_users_of_that_provider_only()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, string name, _) = await CreateTemplateAsync(author, widgets: 3);

        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments", new { scope = new { kind = "Provider", providerId = 100 }, confirmedWidgetCount = 3 })).Status);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/publish")).Status);
        var (previewStatus, preview, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments/preview", new { kind = "Provider", providerId = 100 });
        Assert.Equal(HttpStatusCode.OK, previewStatus);
        Assert.Equal(3, preview.GetProperty("widgetCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("providerCount").GetInt32());
        int assignmentId = await PushToProviderAsync(publisher, id, 100, 3);

        using CookieClient first = Customer(31);
        using CookieClient second = Customer(32);
        using CookieClient elsewhere = Customer(33);
        Assert.Contains(name, await ViewNamesAsync(first));
        Assert.Contains(name, await ViewNamesAsync(second));
        Assert.Equal(1, (await ViewNamesAsync(first)).Count(n => n == name));
        var (otherProvider, _, _, _) = await CallAsync(elsewhere, HttpMethod.Get, Views);
        Assert.Equal(HttpStatusCode.NotFound, otherProvider);

        var (listStatus, list, _, _) = await CallAsync(publisher, HttpMethod.Get, $"{Templates}/{id}/assignments");
        Assert.Equal(HttpStatusCode.OK, listStatus);
        JsonElement row = Assert.Single(list.GetProperty("assignments").EnumerateArray());
        Assert.Equal(assignmentId, row.GetProperty("id").GetInt32());
        Assert.Equal(2, row.GetProperty("deliveredCount").GetInt32());
        Assert.False(list.GetRawText().Contains("user31", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_revoked_push_reaches_nobody_new_and_the_copies_already_delivered_stay()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, string name, _) = await CreateTemplateAsync(author);
        await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/publish");
        int assignmentId = await PushToProviderAsync(publisher, id, 100, 2);
        using CookieClient first = Customer(31);
        Assert.Contains(name, await ViewNamesAsync(first));

        var (status, body, _, _) = await CallAsync(publisher, HttpMethod.Delete, $"/api/v1/admin/template-assignments/{assignmentId}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("revokedAt").ValueKind);
        using CookieClient second = Customer(32);
        Assert.DoesNotContain(name, await ViewNamesAsync(second));
        Assert.Contains(name, await ViewNamesAsync(first));
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(publisher, HttpMethod.Delete, $"/api/v1/admin/template-assignments/{assignmentId}")).Status);
    }

    [Fact]
    public async Task A_facility_limited_user_is_treated_as_not_having_the_provider_and_gets_no_delivery()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, _, _) = await CreateTemplateAsync(author);
        await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/publish");
        await PushToProviderAsync(publisher, id, 100, 2);
        using CookieClient limited = Customer(34);

        var (status, _, _, _) = await CallAsync(limited, HttpMethod.Get, Views);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_stale_confirmation_a_draft_and_a_pushed_template_delete_are_all_conflicts()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, _, _) = await CreateTemplateAsync(author, widgets: 2);
        await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/publish");

        var (stale, _, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments", new { scope = new { kind = "Provider", providerId = 100 }, confirmedWidgetCount = 9 });
        await PushToProviderAsync(publisher, id, 100, 2);
        var (delete, _, _, _) = await CallAsync(author, HttpMethod.Delete, $"{Templates}/{id}");
        var (unpublished, _, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/unpublish");
        var (draftPush, _, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments", new { scope = new { kind = "Provider", providerId = 100 }, confirmedWidgetCount = 2 });

        Assert.Equal(HttpStatusCode.Conflict, stale);
        Assert.Equal(HttpStatusCode.Conflict, delete);
        Assert.Equal(HttpStatusCode.OK, unpublished);
        Assert.Equal(HttpStatusCode.Conflict, draftPush);
    }

    [Fact]
    public async Task Saving_needs_the_version_and_a_stale_or_made_up_one_changes_nothing()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        (int id, string name, string etag) = await CreateTemplateAsync(author);
        object update = new { name = name + "-2", layout = Layout() };

        Assert.Equal((HttpStatusCode)428, (await CallAsync(author, HttpMethod.Put, $"{Templates}/{id}", update)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await CallAsync(author, HttpMethod.Put, $"{Templates}/{id}", update, "\"abc\"")).Status);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(author, HttpMethod.Put, $"{Templates}/{id}", update, etag)).Status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await CallAsync(author, HttpMethod.Put, $"{Templates}/{id}", new { name = "stale", layout = Layout() }, etag)).Status);
        var (_, body, _, _) = await CallAsync(author, HttpMethod.Get, $"{Templates}/{id}");
        Assert.Equal(name + "-2", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_bad_layout_returns_codes_and_never_the_values_sent_or_a_log_line_with_them()
    {
        using CookieClient author = AdminClient(AuthorOnly);

        var (status, _, text, _) = await CallAsync(author, HttpMethod.Post, Templates, new { name = Unique(), layout = Layout(1, Marker) });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("unknown_metric", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Marker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"kind":"All","providerId":100}""")]
    [InlineData("""{"kind":"Provider"}""")]
    [InlineData("""{"kind":"Provider","providerId":0}""")]
    [InlineData("""{"kind":"Provider","providerId":-5}""")]
    [InlineData("""{"kind":"Mercy Air"}""")]
    [InlineData("""{"kind":"all"}""")]
    [InlineData("""{}""")]
    public async Task An_audience_that_is_not_all_or_a_numbered_provider_is_refused(string scope)
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, _, _) = await CreateTemplateAsync(author);
        await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/publish");

        var (preview, _, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments/preview", scope);
        var (create, _, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments", $$"""{"scope":{{scope}},"confirmedWidgetCount":2}""");

        Assert.Equal(HttpStatusCode.BadRequest, preview);
        Assert.Equal(HttpStatusCode.BadRequest, create);
    }

    [Fact]
    public async Task A_push_to_everyone_shows_no_provider_count_because_prism_has_no_list_of_providers()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        using CookieClient publisher = AdminClient(PublisherOnly);
        (int id, _, _) = await CreateTemplateAsync(author);

        var (status, body, _, _) = await CallAsync(publisher, HttpMethod.Post, $"{Templates}/{id}/assignments/preview", new { kind = "All" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("providerCount").ValueKind);
    }

    [Fact]
    public async Task Owner_provider_and_publication_in_a_body_are_ignored_when_creating_a_template()
    {
        using CookieClient author = AdminClient(AuthorOnly);

        var (status, body, _, _) = await CallAsync(author, HttpMethod.Post, Templates,
            new { name = Unique(), layout = Layout(), isPublished = true, providerId = 100, ownerId = 5, @internal = true });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Authoring_a_template_writes_audit_rows_a_customer_read_does_not_need()
    {
        using CookieClient author = AdminClient(AuthorOnly);
        (int id, _, _) = await CreateTemplateAsync(author);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Prism.Infrastructure.Persistence.PrismDbContext>();
        Assert.True(await db.AdminAuditEntries.AnyAsync(e => e.ObjectId == id && e.ActorPortalUserId > 0, TestContext.Current.CancellationToken));
    }

    private sealed class Divisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId == 100 ? new HashSet<string> { "9", "10" } : []);

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingDivision>>([]);
    }
}
