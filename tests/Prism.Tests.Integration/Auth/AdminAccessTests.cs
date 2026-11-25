using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Prism.Api.Admin;
using Prism.Application.Auth;

namespace Prism.Tests.Integration.Auth;

/// <summary>The internal admin area needs the portal group and a tier. Either alone grants nothing.</summary>
public sealed class AdminAccessTests : IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;

    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_member_of_the_group_with_a_tier_reaches_the_admin_session_and_sees_only_the_tiers_they_hold()
    {
        using CookieClient client = factory.NewSignedInClientInGroups([Group], Permissions.App, Permissions.AdminTemplatesPublish);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal([Permissions.AdminTemplatesPublish], json.RootElement.GetProperty("tiers").EnumerateArray().Select(t => t.GetString()!).ToArray());
    }

    [Fact]
    public async Task A_customer_user_who_somehow_holds_a_tier_without_the_group_is_refused()
    {
        using CookieClient client = factory.NewSignedInClientInGroups(["Testing group"], Permissions.App, Permissions.AdminManage);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_session_that_names_a_tier_but_has_no_internal_claim_is_refused_by_the_policy_itself()
    {
        // NewSignedInClient issues the session with the permission list as given and the user has no groups, so no internal claim.
        using CookieClient client = factory.NewSignedInClient(Permissions.App, Permissions.AdminManage);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_internal_user_with_no_tier_can_open_the_session_route_and_sees_no_tiers()
    {
        using CookieClient client = factory.NewSignedInClientInGroups([Group], Permissions.App);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(json.RootElement.GetProperty("tiers").EnumerateArray());
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_the_admin_route()
    {
        using CookieClient client = factory.NewClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("X-Internal", "true")]
    [InlineData("X-Prism-Internal", "1")]
    public async Task A_request_header_cannot_make_a_customer_internal(string header, string value)
    {
        using CookieClient client = factory.NewSignedInClient(Permissions.App, Permissions.AdminManage);

        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, "/api/v1/admin/session", null, new Dictionary<string, string> { [header] = value });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_query_string_flag_cannot_make_a_customer_internal()
    {
        using CookieClient client = factory.NewSignedInClient(Permissions.App, Permissions.AdminManage);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/session?internal=true&isInternal=true");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_reports_internal_membership_and_tiers_only_for_a_member_of_the_group()
    {
        using CookieClient member = factory.NewSignedInClientInGroups([Group], Permissions.App, Permissions.AdminCustomersView);
        using CookieClient customer = factory.NewSignedInClientInGroups([], Permissions.App, Permissions.AdminCustomersView);

        using JsonDocument memberMe = JsonDocument.Parse(await (await member.GetAsync("/api/v1/me")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using JsonDocument customerMe = JsonDocument.Parse(await (await customer.GetAsync("/api/v1/me")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.True(memberMe.RootElement.GetProperty("isInternal").GetBoolean());
        Assert.Equal([Permissions.AdminCustomersView], memberMe.RootElement.GetProperty("adminPermissions").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.DoesNotContain(Permissions.AdminCustomersView, memberMe.RootElement.GetProperty("permissions").EnumerateArray().Select(t => t.GetString()!));
        Assert.False(customerMe.RootElement.GetProperty("isInternal").GetBoolean());
        Assert.Empty(customerMe.RootElement.GetProperty("adminPermissions").EnumerateArray());
    }

    [Fact]
    public async Task A_cookie_or_body_flag_cannot_make_a_customer_internal()
    {
        using CookieClient client = factory.NewSignedInClient(Permissions.App, Permissions.AdminManage);
        client.Set("internal", "true");
        client.Set("isInternal", "true");

        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, "/api/v1/admin/session", new StringContent("""{"internal":true,"isInternal":true}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("EMSbilling.com Users")]
    public void The_group_name_is_optional_because_the_marker_role_is_the_normal_signal(string groupName)
    {
        ValidateOptionsResult result = new InternalAccessOptionsValidator().Validate(null, new InternalAccessOptions { GroupName = groupName });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_group_name_that_is_too_long_is_refused_in_any_environment()
    {
        var validator = new InternalAccessOptionsValidator();

        ValidateOptionsResult result = validator.Validate(null, new InternalAccessOptions { GroupName = new string('g', InternalAccessOptions.MaxGroupNameLength + 1) });

        Assert.False(result.Succeeded);
    }
}
