using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Prism.Api.Customers;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Auth;

namespace Prism.Tests.Integration.Auth;

/// <summary>The customer permission policy: a permission comes from the user's Customer Role in the route's Provider, never from the session.</summary>
public sealed class CustomerPermissionHandlerTests
{
    private sealed class Access(Func<int, long, IReadOnlySet<string>> perProvider, IReadOnlySet<string>? any = null, bool fails = false) : ICustomerAccess
    {
        public Task<IReadOnlySet<string>> PermissionsAsync(int providerId, long portalUserId, CancellationToken cancellationToken) =>
            fails ? throw new InvalidOperationException("store down") : Task.FromResult(perProvider(providerId, portalUserId));

        public Task<IReadOnlySet<string>> PermissionsInAnyProviderAsync(long portalUserId, CancellationToken cancellationToken) =>
            fails ? throw new InvalidOperationException("store down") : Task.FromResult(any ?? new HashSet<string>());

        public Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> RecordAsync(long portalUserId, string displayName, IReadOnlyList<int> providerIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static HashSet<string> Set(params string[] permissions) => [.. permissions];

    private static async Task<(bool Succeeded, ClaimsPrincipal User)> RunAsync(
        string permission,
        ICustomerAccess access,
        string? providerId,
        IEnumerable<Claim>? claims = null,
        bool internalUser = false)
    {
        List<Claim> all = [new(SessionTokenIssuer.PortalUserIdClaim, "7"), .. claims ?? []];
        if (internalUser)
        {
            all.Add(new Claim(SessionTokenIssuer.InternalClaim, "true"));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(all, "test"));
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(access).BuildServiceProvider() };
        if (providerId is not null)
        {
            http.Request.RouteValues["providerId"] = providerId;
        }

        var requirement = new CustomerPermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, http);
        await new CustomerPermissionHandler().HandleAsync(context);
        return (context.HasSucceeded, user);
    }

    [Fact]
    public async Task The_role_in_the_routes_provider_decides()
    {
        var access = new Access((provider, _) => provider == 100 ? Set(Permissions.ClaimsView) : Set(Permissions.DashboardView));

        Assert.True((await RunAsync(Permissions.ClaimsView, access, "100")).Succeeded);
        Assert.False((await RunAsync(Permissions.ClaimsView, access, "200")).Succeeded);
    }

    [Fact]
    public async Task A_permission_in_the_session_is_not_enough()
    {
        var access = new Access((_, _) => Set(Permissions.DashboardView));

        (bool ok, _) = await RunAsync(Permissions.ClaimsView, access, "100", [new Claim(SessionTokenIssuer.PermissionsClaim, Permissions.ClaimsView)]);

        Assert.False(ok);
    }

    [Fact]
    public async Task A_role_in_another_provider_does_not_count_for_this_one()
    {
        var access = new Access((_, _) => Set(), any: Set(Permissions.ClaimsView));

        Assert.False((await RunAsync(Permissions.ClaimsView, access, "100")).Succeeded);
    }

    [Fact]
    public async Task A_route_with_no_provider_uses_what_the_user_holds_in_any_of_theirs()
    {
        var access = new Access((_, _) => Set(), any: Set(Permissions.DashboardView));

        Assert.True((await RunAsync(Permissions.DashboardView, access, null)).Succeeded);
        Assert.False((await RunAsync(Permissions.ClaimsView, access, null)).Succeeded);
    }

    [Fact]
    public async Task A_provider_that_is_not_a_number_passes_so_the_route_answers_not_found()
    {
        var access = new Access((_, _) => Set());

        Assert.True((await RunAsync(Permissions.DashboardView, access, "abc")).Succeeded);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_denies()
    {
        var access = new Access((_, _) => Set(Permissions.DashboardView), fails: true);

        Assert.False((await RunAsync(Permissions.DashboardView, access, "100")).Succeeded);
        Assert.False((await RunAsync(Permissions.DashboardView, access, null)).Succeeded);
    }

    [Fact]
    public async Task A_user_with_no_record_is_denied()
    {
        var access = new Access((_, _) => Set());

        Assert.False((await RunAsync(Permissions.DashboardView, access, "100")).Succeeded);
    }

    [Fact]
    public async Task The_permissions_held_in_this_provider_are_added_for_this_request_only()
    {
        var access = new Access((_, _) => Set(Permissions.DashboardView, Permissions.ViewsManage));

        (_, ClaimsPrincipal user) = await RunAsync(Permissions.DashboardView, access, "100");

        Assert.True(user.HasClaim(SessionTokenIssuer.PermissionsClaim, Permissions.ViewsManage));
        Assert.False(user.HasClaim(SessionTokenIssuer.PermissionsClaim, Permissions.ClaimsView));
    }

    [Fact]
    public async Task An_internal_user_holds_no_customer_role_even_if_the_store_has_one()
    {
        var access = new Access((_, _) => Set(Permissions.DashboardView, Permissions.ClaimsView));

        Assert.False((await RunAsync(Permissions.DashboardView, access, "100", internalUser: true)).Succeeded);
        Assert.False((await RunAsync(Permissions.ClaimsView, access, "100", [new Claim(SessionTokenIssuer.PermissionsClaim, Permissions.AdminCustomersView)], internalUser: true)).Succeeded);
    }

    [Fact]
    public async Task Only_the_customer_data_viewer_tier_lets_an_internal_user_pass_the_dashboard_policy()
    {
        var access = new Access((_, _) => Set());

        Assert.True((await RunAsync(Permissions.DashboardView, access, "100", [new Claim(SessionTokenIssuer.PermissionsClaim, Permissions.AdminCustomersView)], internalUser: true)).Succeeded);
        Assert.False((await RunAsync(Permissions.DashboardView, access, "100", [new Claim(SessionTokenIssuer.PermissionsClaim, Permissions.AdminManage)], internalUser: true)).Succeeded);
    }

    [Fact]
    public async Task A_session_with_no_user_id_is_denied()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<ICustomerAccess>(new Access((_, _) => Set(Permissions.DashboardView))).BuildServiceProvider() };
        var context = new AuthorizationHandlerContext([new CustomerPermissionRequirement(Permissions.DashboardView)], user, http);

        await new CustomerPermissionHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
