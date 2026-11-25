using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Auth;
using Prism.Infrastructure.Customers;

namespace Prism.Api.Customers;

/// <summary>Registers Customer Roles: the per-Provider permission policies, the role store and the user-management routes.</summary>
public static class CustomerAccessServiceCollectionExtensions
{
    public static IServiceCollection AddPrismCustomerAccess(this IServiceCollection services)
    {
        services.AddSingleton<CustomerRoleCache>();
        services.AddScoped<CustomerAccessStore>();
        services.AddScoped<ICustomerAccess>(sp => sp.GetRequiredService<CustomerAccessStore>());
        services.AddScoped<ICustomerUserAdmin>(sp => sp.GetRequiredService<CustomerAccessStore>());

        services.AddSingleton<IAuthorizationHandler, CustomerPermissionHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Permissions.DashboardView, policy => policy.AddRequirements(new CustomerPermissionRequirement(Permissions.DashboardView)))
            .AddPolicy(Permissions.ClaimsView, policy => policy.AddRequirements(new CustomerPermissionRequirement(Permissions.ClaimsView)))
            .AddPolicy(Permissions.ViewsManage, policy => policy.AddRequirements(new CustomerPermissionRequirement(Permissions.ViewsManage)))
            .AddPolicy(Permissions.UsersManage, policy => policy.AddRequirements(new CustomerPermissionRequirement(Permissions.UsersManage)));
        return services;
    }

    public static void MapCustomerAccessEndpoints(this RouteGroupBuilder v1)
    {
        CustomerUserEndpoints.Map(v1);
        StaffCustomerEndpoints.Map(v1);
    }
}

/// <summary>A customer permission, checked against the user's Customer Role in the Provider named in the route.</summary>
public sealed record CustomerPermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Passes when the signed-in user's Customer Role in the route's Provider carries the permission. The permission never comes from the session. A route with
/// no Provider uses the permissions the user holds in any of theirs. A missing record, an unreadable store or a Provider that is not a number all deny.
/// The permissions held in this Provider are added to the request's principal (only for this request), so later checks in the same request see the same answer.
/// An Internal User holds no customer role: the one exception is the customer data viewer tier, which may read aggregate figures through the audited preview.
/// </summary>
public sealed class CustomerPermissionHandler : AuthorizationHandler<CustomerPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CustomerPermissionRequirement requirement)
    {
        ClaimsPrincipal user = context.User;
        if (user.HasClaim(SessionTokenIssuer.InternalClaim, "true"))
        {
            if (requirement.Permission == Permissions.DashboardView && user.HasClaim(SessionTokenIssuer.PermissionsClaim, Permissions.AdminCustomersView))
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (context.Resource is not HttpContext http
            || !long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), System.Globalization.CultureInfo.InvariantCulture, out long portalUserId))
        {
            return;
        }

        // A Provider that is not a number matches no Provider: the route's own Provider check answers 404, the same as for any Provider the user is not assigned to.
        if (http.GetRouteValue("providerId") is string rawProvider && !int.TryParse(rawProvider, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            context.Succeed(requirement);
            return;
        }

        var access = http.RequestServices.GetRequiredService<ICustomerAccess>();
        IReadOnlySet<string> held;
        try
        {
            held = http.GetRouteValue("providerId") switch
            {
                string provider => await access.PermissionsAsync(int.Parse(provider, System.Globalization.CultureInfo.InvariantCulture), portalUserId, http.RequestAborted),
                _ => await access.PermissionsInAnyProviderAsync(portalUserId, http.RequestAborted),
            };
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            // Roles that cannot be read mean no permission. Fail closed.
            return;
        }

        if (user.Identity is ClaimsIdentity identity)
        {
            foreach (string permission in held.Where(p => !user.HasClaim(SessionTokenIssuer.PermissionsClaim, p)))
            {
                identity.AddClaim(new Claim(SessionTokenIssuer.PermissionsClaim, permission));
            }
        }

        if (held.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
