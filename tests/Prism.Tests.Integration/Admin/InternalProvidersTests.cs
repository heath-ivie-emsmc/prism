using System.Net;
using System.Text.Json;
using Prism.Application.Auth;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Admin;

/// <summary>
/// The portal's company list for the internal group is every company. An Internal User's own scope is therefore not read: they have no customer Providers,
/// and reach customer figures only through the audited preview.
/// </summary>
public sealed class InternalProvidersTests : IDisposable
{
    private readonly AuthTestFactory factory = new();
    private static readonly string[] Customer = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];

    public InternalProvidersTests()
    {
        // What the portal would send for anyone in the group: a long list.
        factory.Directory.ProvidersByUser = _ => [new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Abbeville", [])];
    }

    public void Dispose() => factory.Dispose();

    private static async Task<JsonElement> MeAsync(CookieClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonElement.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_customer_user_gets_the_providers_the_portal_assigned()
    {
        JsonElement me = await MeAsync(factory.NewSignedInClientAs(31, "customer", Customer));

        Assert.Equal(2, me.GetProperty("providers").GetArrayLength());
    }

    [Fact]
    public async Task An_internal_user_gets_no_customer_providers_even_though_the_portal_lists_them()
    {
        JsonElement me = await MeAsync(factory.NewSignedInClientInGroups([AuthTestFactory.InternalGroupName], [.. Customer, Permissions.AdminTemplatesAuthor]));

        Assert.True(me.GetProperty("isInternal").GetBoolean());
        Assert.Equal(0, me.GetProperty("providers").GetArrayLength());
    }

    [Fact]
    public async Task An_internal_user_cannot_use_a_provider_route_with_a_provider_the_portal_listed()
    {
        CookieClient staff = factory.NewSignedInClientInGroups([AuthTestFactory.InternalGroupName], [.. Customer, Permissions.AdminTemplatesAuthor]);
        CookieClient customer = factory.NewSignedInClientAs(31, "customer", Customer);

        Assert.True((await staff.GetAsync("/api/v1/providers/100/metrics/catalog")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/v1/providers/100/metrics/catalog")).StatusCode);
    }

    [Fact]
    public async Task A_group_member_with_no_tier_gets_no_providers_either()
    {
        JsonElement me = await MeAsync(factory.NewSignedInClientInGroups([AuthTestFactory.InternalGroupName], Customer));

        Assert.Equal(0, me.GetProperty("providers").GetArrayLength());
    }
}
