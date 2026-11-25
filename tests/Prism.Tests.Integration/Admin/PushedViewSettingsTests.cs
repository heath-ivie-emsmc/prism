using System.Net;
using System.Text;
using System.Text.Json;
using Prism.Application.Auth;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Admin;

/// <summary>Hiding and restoring pushed views from settings. Customers 51 and 52 are in Provider 100, and 53 is only in Provider 300.</summary>
public sealed class PushedViewSettingsTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;
    private const string Views = "/api/v1/providers/100/saved-views";
    private const string Pushed = "/api/v1/providers/100/settings/pushed-views";
    private static readonly string[] Both = [Permissions.App, Permissions.AdminTemplatesAuthor, Permissions.AdminTemplatesPublish];
    private static readonly string[] CustomerPermissions = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];

    private readonly AuthTestFactory factory;

    public PushedViewSettingsTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = postgres.ConnectionString });
        factory.Directory.ProvidersByUser = user => user == 53 ? [new ProviderScope("300", "Elsewhere", [])] : [new ProviderScope("100", "Mercy Air", [])];
    }

    public void Dispose() => factory.Dispose();

    private static CookieClient WithXsrf(CookieClient client)
    {
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private CookieClient Customer(long user) => WithXsrf(factory.NewSignedInClientAs(user, $"user{user}", CustomerPermissions));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> CallAsync(CookieClient client, HttpMethod method, string url, object? body = null)
    {
        HttpContent? content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.SendAsync(method, url, content, new Dictionary<string, string> { ["X-XSRF-TOKEN"] = "test-xsrf" });
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.StartsWith('{') ? JsonElement.Parse(text) : default);
    }

    /// <summary>Pushes a one-widget template to Provider 100 and returns its name.</summary>
    private async Task<string> PushAsync()
    {
        CookieClient staff = WithXsrf(factory.NewSignedInClientInGroups([Group], Both));
        string name = $"pushed-{Guid.NewGuid():N}"[..24];
        var layout = new { version = 1, density = "comfortable", widgets = new[] { new { id = "w1", metricKey = "net_collections", presentation = "line", x = 0, y = 0, w = 6, h = 3 } } };
        int id = (await CallAsync(staff, HttpMethod.Post, "/api/v1/admin/view-templates", new { name, layout })).Body.GetProperty("id").GetInt32();
        await CallAsync(staff, HttpMethod.Post, $"/api/v1/admin/view-templates/{id}/publish");
        var push = await CallAsync(staff, HttpMethod.Post, $"/api/v1/admin/view-templates/{id}/assignments", new { scope = new { kind = "Provider", providerId = 100 }, confirmedWidgetCount = 1 });
        Assert.Equal(HttpStatusCode.Created, push.Status);
        return name;
    }

    private static async Task<JsonElement[]> MineAsync(CookieClient customer) =>
        [.. (await CallAsync(customer, HttpMethod.Get, Views)).Body.GetProperty("mine").EnumerateArray()];

    [Fact]
    public async Task A_pushed_view_is_marked_hides_and_restores_and_is_not_delivered_again()
    {
        string name = await PushAsync();
        CookieClient customer = Customer(51);

        JsonElement view = Assert.Single(await MineAsync(customer), v => v.GetProperty("name").GetString() == name);
        Assert.True(view.GetProperty("fromTemplate").GetBoolean());
        int id = view.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await CallAsync(customer, HttpMethod.Post, $"{Pushed}/{id}/hide")).Status);
        Assert.DoesNotContain(await MineAsync(customer), v => v.GetProperty("name").GetString() == name);
        JsonElement hidden = Assert.Single((await CallAsync(customer, HttpMethod.Get, Pushed + "/")).Body.GetProperty("mine").EnumerateArray(), v => v.GetProperty("name").GetString() == name);
        Assert.True(hidden.GetProperty("isHidden").GetBoolean());
        // Opening the list again does not bring it back by delivering a second copy.
        Assert.DoesNotContain(await MineAsync(customer), v => v.GetProperty("name").GetString()!.StartsWith(name, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.NoContent, (await CallAsync(customer, HttpMethod.Post, $"{Pushed}/{id}/restore")).Status);
        Assert.Contains(await MineAsync(customer), v => v.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Only_the_owner_can_hide_and_only_a_pushed_view_in_their_own_provider()
    {
        string name = await PushAsync();
        int id = Assert.Single(await MineAsync(Customer(51)), v => v.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(Customer(52), HttpMethod.Post, $"{Pushed}/{id}/hide")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(Customer(53), HttpMethod.Post, $"{Pushed}/{id}/hide")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(Customer(53), HttpMethod.Get, Pushed + "/")).Status);

        // A view the user made themselves is not a pushed view.
        CookieClient owner = Customer(52);
        var layout = new { version = 1, density = "comfortable", widgets = new[] { new { id = "w1", metricKey = "net_collections", presentation = "line", x = 0, y = 0, w = 6, h = 3 } } };
        int own = (await CallAsync(owner, HttpMethod.Post, Views, new { name = $"mine-{Guid.NewGuid():N}"[..20], layout })).Body.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(owner, HttpMethod.Post, $"{Pushed}/{own}/hide")).Status);

        // The first user's view is still listed for them.
        Assert.Contains(await MineAsync(Customer(51)), v => v.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task The_settings_routes_need_the_dashboard_permission()
    {
        CookieClient none = WithXsrf(factory.NewSignedInClientAs(54, "user54", Permissions.App));

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(none, HttpMethod.Get, Pushed + "/")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(none, HttpMethod.Post, $"{Pushed}/1/hide")).Status);
    }
}
