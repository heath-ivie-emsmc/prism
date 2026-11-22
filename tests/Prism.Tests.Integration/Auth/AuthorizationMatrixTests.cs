using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Prism.Tests.Integration.Auth;

/// <summary>Finds every /api route at runtime. A route without a documented reason to be public must refuse an anonymous caller.</summary>
public sealed class AuthorizationMatrixTests : IDisposable
{
    /// <summary>Public routes and why: the sign-in handshake cannot require a session.</summary>
    private static readonly string[] DocumentedAnonymousRoutes =
    [
        "/api/auth/sso/start",
        "/api/auth/sso/callback",
        "/api/auth/sso/confirm",
        "/api/auth/sso/logout",
    ];

    private readonly AuthTestFactory factory = new(allowIdpInitiated: true);

    public void Dispose() => factory.Dispose();

    /// <summary>The route with every parameter, including constrained ones like <c>{viewId:int}</c>, replaced by a value that satisfies it.</summary>
    private static string Concrete(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "1", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private List<(string Route, string[] Methods, bool Anonymous)> ApiRoutes()
    {
        using HttpClient warmup = factory.CreateClient();
        return factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Select(endpoint => (
                endpoint.RoutePattern.RawText!,
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToArray() ?? ["GET"],
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null))
            .ToList();
    }

    [Fact]
    public void Every_anonymous_route_is_documented_and_every_documented_route_exists()
    {
        List<(string Route, string[] Methods, bool Anonymous)> routes = ApiRoutes();

        string[] anonymous = routes.Where(r => r.Anonymous).Select(r => r.Route).Distinct().Order().ToArray();

        Assert.Equal(DocumentedAnonymousRoutes.Order().ToArray(), anonymous);
    }

    [Fact]
    public async Task Every_other_api_route_refuses_an_anonymous_caller()
    {
        using var client = factory.NewClient();
        List<(string Route, string[] Methods, bool Anonymous)> protectedRoutes = ApiRoutes().Where(r => !r.Anonymous).ToList();
        Assert.NotEmpty(protectedRoutes);

        foreach ((string route, string[] methods, _) in protectedRoutes)
        {
            foreach (string method in methods)
            {
                using HttpResponseMessage response = await client.SendAsync(new HttpMethod(method), Concrete(route));

                Assert.True(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                    $"{method} {route} answered {(int)response.StatusCode} to an anonymous caller.");
            }
        }
    }

    [Fact]
    public async Task Health_probes_stay_anonymous()
    {
        using var client = factory.NewClient();

        using HttpResponseMessage up = await client.GetAsync("/up");
        using HttpResponseMessage ready = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private CookieClient ClientWithPermissions(params string[] permissions) => factory.NewSignedInClient(permissions);

    [Fact]
    public async Task A_signed_in_user_without_the_permission_is_refused_on_every_route_that_names_a_policy()
    {
        // Today no route names a policy, so this loop is empty. It starts to bite the day a permission-gated route is added.
        using CookieClient limited = ClientWithPermissions(Prism.Application.Auth.Permissions.App);
        List<(string Route, string[] Methods, string Policy)> gated = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Where(data => !string.IsNullOrEmpty(data.Policy))
                .Select(data => (endpoint.RoutePattern.RawText!, endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToArray() ?? ["GET"], data.Policy!)))
            .ToList();

        foreach ((string route, string[] methods, string policy) in gated)
        {
            foreach (string method in methods)
            {
                using HttpResponseMessage response = await limited.SendAsync(new HttpMethod(method), Concrete(route));

                Assert.True(
                    response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                    $"{method} {route} (policy {policy}) answered {(int)response.StatusCode} to a user without that permission.");
            }
        }
    }

    [Fact]
    public async Task A_signed_in_user_with_only_the_sign_in_permission_can_still_reach_the_routes_that_need_only_a_session()
    {
        using CookieClient limited = ClientWithPermissions(Prism.Application.Auth.Permissions.App);

        using HttpResponseMessage me = await limited.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_dashboard_view_policy_needs_that_permission_in_the_session(bool granted)
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var identity = new System.Security.Claims.ClaimsIdentity(
            granted ? [new System.Security.Claims.Claim(Prism.Infrastructure.Auth.SessionTokenIssuer.PermissionsClaim, Prism.Application.Auth.Permissions.DashboardView)] : [],
            "test");

        AuthorizationResult result = await authorization.AuthorizeAsync(
            new System.Security.Claims.ClaimsPrincipal(identity), Prism.Application.Auth.Permissions.DashboardView);

        Assert.Equal(granted, result.Succeeded);
    }

    [Fact]
    public async Task A_permission_claim_for_a_different_permission_does_not_satisfy_the_dashboard_view_policy()
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(Prism.Infrastructure.Auth.SessionTokenIssuer.PermissionsClaim, Prism.Application.Auth.Permissions.PhiView)],
            "test");

        AuthorizationResult result = await authorization.AuthorizeAsync(
            new System.Security.Claims.ClaimsPrincipal(identity), Prism.Application.Auth.Permissions.DashboardView);

        Assert.False(result.Succeeded);
    }
}
