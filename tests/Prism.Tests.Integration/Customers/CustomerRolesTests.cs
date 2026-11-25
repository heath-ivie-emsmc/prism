using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Customers;

/// <summary>
/// Customer Roles through the real host, the real role store and real PostgreSQL. Users 61 to 66 are in Provider 100, and 67 is only in Provider 200.
/// Each test uses its own Provider number for the records it makes, through the routes, so tests do not share state.
/// </summary>
public sealed class CustomerRolesTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;
    private static readonly string[] Staff = [Permissions.App, Permissions.AdminManage];

    private readonly AuthTestFactory factory;

    public CustomerRolesTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = postgres.ConnectionString },
            configureServices: AuthTestFactory.UseRealCustomerAccess);
        factory.Directory.ProvidersByUser = user => user >= 7000 ? [new ProviderScope(ProviderFor(user), "Provider", [])] : [];
    }

    public void Dispose() => factory.Dispose();

    // Every test works in its own Provider: the user ids encode it (id = provider * 10 + slot) so the directory can answer without sharing state.
    private static string ProviderFor(long user) => (user / 10).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int next = 7000;

    private static (int Provider, long First) NewProvider()
    {
        int provider = Interlocked.Increment(ref next);
        return (provider, provider * 10L);
    }

    private CookieClient Customer(long user, params string[] portalRoles)
    {
        var portalUser = new PortalUser($"user{user}", $"User {user}", user, portalRoles.Length > 0 ? portalRoles : ["SomethingElse"], []);
        string session = factory.Services.GetRequiredService<ISessionTokenIssuer>().Issue(portalUser, [Permissions.App]);
        CookieClient client = factory.NewClient();
        client.Set("prism_session", session);
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private CookieClient Internal(params string[] permissions)
    {
        CookieClient client = factory.NewSignedInClientInGroups([Group], permissions);
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> CallAsync(CookieClient client, HttpMethod method, string url, object? body = null)
    {
        HttpContent? content = body is null ? null : new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.SendAsync(method, url, content, new Dictionary<string, string> { ["X-XSRF-TOKEN"] = "test-xsrf" });
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.StartsWith('{') ? JsonElement.Parse(text) : default, text);
    }

    /// <summary>Opens the app as this user, which is when Prism first records them.</summary>
    private static async Task<JsonElement> SignInAsync(CookieClient client)
    {
        var (status, body, text) = await CallAsync(client, HttpMethod.Get, "/api/v1/me");
        Assert.True(status == HttpStatusCode.OK, text);
        return body;
    }

    private static string[] PermissionsIn(JsonElement me) => [.. me.GetProperty("providers")[0].GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];

    private async Task SetRoleByStaffAsync(int provider, long user, string role)
    {
        var (status, _, text) = await CallAsync(Internal(Staff), HttpMethod.Put, $"/api/v1/admin/customers/{provider}/users/{user}/role", new { roleKey = role });
        Assert.True(status == HttpStatusCode.OK, text);
    }

    private async Task<long> AuditCountAsync(int provider)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PrismDbContext>().AdminAuditEntries.LongCountAsync(a => a.ProviderId == provider, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_new_user_is_recorded_at_first_sign_in_with_the_default_role()
    {
        (int provider, long first) = NewProvider();

        JsonElement me = await SignInAsync(Customer(first));

        Assert.Equal([Permissions.DashboardView], PermissionsIn(me));
        Assert.Equal(["prism.app"], me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
        Assert.Equal(1, (await ListAsync(Internal(Staff), provider)).GetProperty("users").GetArrayLength());
    }

    [Fact]
    public async Task The_staff_default_role_is_given_to_users_first_recorded_afterwards()
    {
        (int provider, long first) = NewProvider();
        CookieClient staff = Internal(Staff);
        var (status, _, text) = await CallAsync(staff, HttpMethod.Put, $"/api/v1/admin/customers/{provider}/settings", new { defaultRoleKey = "analyst", allowAdminRestrictedGrants = false });
        Assert.True(status == HttpStatusCode.OK, text);

        JsonElement me = await SignInAsync(Customer(first));

        Assert.Contains(Permissions.ViewsManage, PermissionsIn(me));
        Assert.Equal(1, await AuditCountAsync(provider));
    }

    [Theory]
    [InlineData("provider_admin")]
    [InlineData("claims_reviewer")]
    [InlineData("nonsense")]
    public async Task A_default_role_must_be_an_ordinary_role_that_cannot_manage_users(string role)
    {
        (int provider, _) = NewProvider();

        var (status, _, _) = await CallAsync(Internal(Staff), HttpMethod.Put, $"/api/v1/admin/customers/{provider}/settings", new { defaultRoleKey = role, allowAdminRestrictedGrants = false });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(0, await AuditCountAsync(provider));
    }

    private static async Task<JsonElement> ListAsync(CookieClient client, int provider, bool staff = true)
    {
        string url = staff ? $"/api/v1/admin/customers/{provider}/users" : $"/api/v1/providers/{provider}/users";
        var (status, body, text) = await CallAsync(client, HttpMethod.Get, url);
        Assert.True(status == HttpStatusCode.OK, text);
        return body;
    }

    [Fact]
    public async Task Staff_make_the_first_admin_and_the_admin_manages_their_own_provider()
    {
        (int provider, long first) = NewProvider();
        CookieClient admin = Customer(first);
        CookieClient colleague = Customer(first + 1);
        await SignInAsync(admin);
        await SignInAsync(colleague);

        // Nobody can manage users until staff make an admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(admin, HttpMethod.Get, $"/api/v1/providers/{provider}/users")).Status);
        await SetRoleByStaffAsync(provider, first, CustomerRoles.ProviderAdmin);

        JsonElement list = await ListAsync(admin, provider, staff: false);
        Assert.Equal(2, list.GetProperty("users").GetArrayLength());
        Assert.Contains(list.GetProperty("users").EnumerateArray(), u => u.GetProperty("isYou").GetBoolean() && u.GetProperty("portalUserId").GetInt64() == first);

        var (status, body, text) = await CallAsync(admin, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first + 1}/role", new { roleKey = "analyst" });
        Assert.True(status == HttpStatusCode.OK, text);
        Assert.Equal("analyst", body.GetProperty("roleKey").GetString());
        Assert.Equal(2, await AuditCountAsync(provider));
    }

    [Fact]
    public async Task A_role_change_reaches_the_users_next_request()
    {
        (int provider, long first) = NewProvider();
        CookieClient colleague = Customer(first + 1);
        await SignInAsync(Customer(first));
        await SignInAsync(colleague);
        string views = $"/api/v1/providers/{provider}/saved-views";

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(colleague, HttpMethod.Post, views, new { name = "x", layout = new { version = 1, density = "comfortable", widgets = Array.Empty<object>() } })).Status);
        await SetRoleByStaffAsync(provider, first + 1, CustomerRoles.Analyst);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await CallAsync(colleague, HttpMethod.Post, views, new { name = "x", layout = new { version = 1, density = "comfortable", widgets = Array.Empty<object>() } })).Status);
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_role_or_demote_the_last_admin()
    {
        (int provider, long first) = NewProvider();
        CookieClient admin = Customer(first);
        CookieClient other = Customer(first + 1);
        await SignInAsync(admin);
        await SignInAsync(other);
        await SetRoleByStaffAsync(provider, first, CustomerRoles.ProviderAdmin);

        var own = await CallAsync(admin, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first}/role", new { roleKey = "viewer" });
        Assert.Equal(HttpStatusCode.Conflict, own.Status);

        // A second admin exists only after staff add one, and then either may demote the other, but not both.
        await SetRoleByStaffAsync(provider, first + 1, CustomerRoles.ProviderAdmin);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(admin, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first + 1}/role", new { roleKey = "viewer" })).Status);
        await SetRoleByStaffAsync(provider, first + 1, CustomerRoles.ProviderAdmin);
        await SetRoleByStaffAsync(provider, first, CustomerRoles.Viewer);
        // `other` is now the only admin, so a demotion by an admin is refused (here by a third admin-less path: staff can, an admin cannot).
        CookieClient third = Customer(first + 2);
        await SignInAsync(third);
        await SetRoleByStaffAsync(provider, first + 2, CustomerRoles.ProviderAdmin);
        await SetRoleByStaffAsync(provider, first + 1, CustomerRoles.Viewer);
        var last = await CallAsync(third, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first + 2}/role", new { roleKey = "viewer" });
        Assert.Equal(HttpStatusCode.Conflict, last.Status);
    }

    [Fact]
    public async Task A_restricted_role_needs_staff_until_staff_allow_the_providers_admins_to_give_it()
    {
        (int provider, long first) = NewProvider();
        CookieClient admin = Customer(first);
        await SignInAsync(admin);
        await SignInAsync(Customer(first + 1));
        await SetRoleByStaffAsync(provider, first, CustomerRoles.ProviderAdmin);
        string url = $"/api/v1/providers/{provider}/users/{first + 1}/role";

        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(admin, HttpMethod.Put, url, new { roleKey = "claims_reviewer" })).Status);
        JsonElement roles = (await ListAsync(admin, provider, staff: false)).GetProperty("roles");
        Assert.False(roles.EnumerateArray().First(r => r.GetProperty("key").GetString() == "claims_reviewer").GetProperty("assignable").GetBoolean());

        await CallAsync(Internal(Staff), HttpMethod.Put, $"/api/v1/admin/customers/{provider}/settings", new { defaultRoleKey = "viewer", allowAdminRestrictedGrants = true });

        Assert.Equal(HttpStatusCode.OK, (await CallAsync(admin, HttpMethod.Put, url, new { roleKey = "claims_reviewer" })).Status);
    }

    [Fact]
    public async Task A_delegation_switch_or_a_user_or_a_provider_sent_in_a_role_body_is_ignored()
    {
        (int provider, long first) = NewProvider();
        CookieClient admin = Customer(first);
        await SignInAsync(admin);
        await SignInAsync(Customer(first + 1));
        await SetRoleByStaffAsync(provider, first, CustomerRoles.ProviderAdmin);

        var (status, body, _) = await CallAsync(admin, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first + 1}/role",
            """{"roleKey":"analyst","allowAdminRestrictedGrants":true,"providerId":1,"portalUserId":1,"permissions":["prism.phi.view"]}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(first + 1, body.GetProperty("portalUserId").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(admin, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first + 1}/role", new { roleKey = "phi_reviewer" })).Status);
    }

    [Fact]
    public async Task Unknown_roles_and_users_who_have_not_signed_in_are_refused_and_nothing_is_audited()
    {
        (int provider, long first) = NewProvider();
        await SignInAsync(Customer(first));
        CookieClient staff = Internal(Staff);

        Assert.Equal(HttpStatusCode.BadRequest, (await CallAsync(staff, HttpMethod.Put, $"/api/v1/admin/customers/{provider}/users/{first}/role", new { roleKey = "root" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await CallAsync(staff, HttpMethod.Put, $"/api/v1/admin/customers/{provider}/users/{first}/role", "{}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(staff, HttpMethod.Put, $"/api/v1/admin/customers/{provider}/users/{first + 9}/role", new { roleKey = "analyst" })).Status);
        Assert.Equal(0, await AuditCountAsync(provider));
    }

    [Fact]
    public async Task An_admin_of_one_provider_cannot_see_or_change_another_providers_users()
    {
        (int providerA, long a) = NewProvider();
        (int providerB, long b) = NewProvider();
        CookieClient adminA = Customer(a);
        await SignInAsync(adminA);
        await SignInAsync(Customer(b));
        await SetRoleByStaffAsync(providerA, a, CustomerRoles.ProviderAdmin);

        Assert.Contains((await CallAsync(adminA, HttpMethod.Get, $"/api/v1/providers/{providerB}/users")).Status, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        Assert.Contains((await CallAsync(adminA, HttpMethod.Put, $"/api/v1/providers/{providerB}/users/{b}/role", new { roleKey = "analyst" })).Status, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        Assert.Equal("viewer", (await ListAsync(Internal(Staff), providerB)).GetProperty("users")[0].GetProperty("roleKey").GetString());
    }

    [Fact]
    public async Task The_staff_routes_need_the_admin_manager_tier_and_the_group()
    {
        (int provider, long first) = NewProvider();
        CookieClient admin = Customer(first);
        await SignInAsync(admin);
        await SetRoleByStaffAsync(provider, first, CustomerRoles.ProviderAdmin);
        string url = $"/api/v1/admin/customers/{provider}/users";

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(admin, HttpMethod.Get, url)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(Internal(Permissions.App, Permissions.AdminTemplatesAuthor), HttpMethod.Get, url)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(factory.NewSignedInClient(Staff), HttpMethod.Get, url)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(factory.NewClient(), HttpMethod.Get, url)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(admin, HttpMethod.Put, $"{url}/{first}/role", new { roleKey = "viewer" })).Status);
    }

    [Fact]
    public async Task A_user_without_the_users_permission_is_refused_and_staff_are_not_customer_users()
    {
        (int provider, long first) = NewProvider();
        CookieClient viewer = Customer(first);
        await SignInAsync(viewer);

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(viewer, HttpMethod.Get, $"/api/v1/providers/{provider}/users")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(viewer, HttpMethod.Put, $"/api/v1/providers/{provider}/users/{first}/role", new { roleKey = "provider_admin" })).Status);

        CookieClient staff = Internal(Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(staff, HttpMethod.Get, $"/api/v1/providers/{provider}/users")).Status);
        // A staff member opening the app is never recorded as a customer user.
        await SignInAsync(staff);
        Assert.Equal(1, (await ListAsync(Internal(Staff), provider)).GetProperty("users").GetArrayLength());
    }

    [Fact]
    public async Task A_role_change_with_an_oversized_body_is_refused_and_changes_nothing()
    {
        (int provider, long first) = NewProvider();
        await SignInAsync(Customer(first));
        string oversized = "{\"roleKey\":\"analyst\",\"padding\":\"" + new string('x', CustomerAccessLimits.MaxRequestBodyBytes) + "\"}";

        var (status, _, _) = await CallAsync(Internal(Staff), HttpMethod.Put, $"/api/v1/admin/customers/{provider}/users/{first}/role", oversized);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal("viewer", (await ListAsync(Internal(Staff), provider)).GetProperty("users")[0].GetProperty("roleKey").GetString());
    }

    [Fact]
    public async Task A_list_is_one_bounded_page()
    {
        (int provider, _) = NewProvider();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            PrismDbContext db = scope.ServiceProvider.GetRequiredService<PrismDbContext>();
            db.ProviderUsers.AddRange(Enumerable.Range(0, CustomerAccessLimits.PageSize + 5).Select(i => new ProviderUser
            {
                ProviderId = provider,
                PortalUserId = 900_000 + i,
                DisplayName = $"Person {i:D3}",
                RoleKey = CustomerRoles.Viewer,
                LastSignInTimestamp = DateTimeOffset.UtcNow,
            }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        JsonElement first = await ListAsync(Internal(Staff), provider);
        var (_, second, _) = await CallAsync(Internal(Staff), HttpMethod.Get, $"/api/v1/admin/customers/{provider}/users?page=1");

        Assert.Equal(CustomerAccessLimits.PageSize, first.GetProperty("users").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(5, second.GetProperty("users").GetArrayLength());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task A_role_change_is_audited_with_actor_target_provider_and_codes_only()
    {
        (int provider, long first) = NewProvider();
        await SignInAsync(Customer(first));

        await SetRoleByStaffAsync(provider, first, CustomerRoles.Analyst);

        using IServiceScope scope = factory.Services.CreateScope();
        AdminAuditEntry entry = await scope.ServiceProvider.GetRequiredService<PrismDbContext>().AdminAuditEntries.SingleAsync(a => a.ProviderId == provider, TestContext.Current.CancellationToken);
        Assert.Equal("customer_role.changed", entry.Action);
        Assert.Equal(first, entry.TargetPortalUserId);
        Assert.Equal("viewer>analyst", entry.Detail);
    }

    [Fact]
    public async Task A_session_issued_before_roles_with_customer_permissions_does_not_keep_them()
    {
        (int provider, long first) = NewProvider();
        // The old kind of session: customer permissions in the cookie. They are stripped, and the user has no role to give them back.
        var portalUser = new PortalUser("old", "Old Session", first, ["SomethingElse"], []);
        string session = factory.Services.GetRequiredService<ISessionTokenIssuer>().Issue(portalUser, [Permissions.App, Permissions.DashboardView, Permissions.ClaimsView, Permissions.PhiView]);
        CookieClient old = factory.NewClient();
        old.Set("prism_session", session);

        var (status, _, _) = await CallAsync(old, HttpMethod.Get, $"/api/v1/providers/{provider}/claims/attention/counts?period=202610");

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }
}
