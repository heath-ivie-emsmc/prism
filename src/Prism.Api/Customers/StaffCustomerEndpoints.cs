using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Auth;
using Prism.Api.SavedViews;

namespace Prism.Api.Customers;

/// <summary>
/// EMS staff managing a customer Provider's roles. The Provider is named in the route as a number and is never inferred. Needs the admin manager tier on top
/// of the internal group. Every change is audited. The acting user is the session's, and a switch or role sent for anyone else is ignored.
/// </summary>
internal static class StaffCustomerEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/admin/customers/{providerId:int}")
            .RequireAuthorization(Permissions.AdminManage)
            .WithTags("Admin");

        group.MapGet("/users", List)
            .WithName("AdminListProviderUsers")
            .WithSummary("A Provider's users and their roles")
            .WithDescription("One page of 50 users who have signed in to the named Provider, with the role each holds. Every role is assignable by staff. `page` starts at 0. Needs the admin manager tier.")
            .Produces<ProviderUsersResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/users/{userId:long}/role", SetRole)
            .WithMetadata(new RequestSizeLimitMetadata(CustomerAccessLimits.MaxRequestBodyBytes))
            .WithName("AdminSetProviderUserRole")
            .WithSummary("Change a user's role in a Provider")
            .WithDescription("Example body: `{\"roleKey\":\"provider_admin\"}`. Staff can give any role, including the first Provider admin and the restricted claim and PHI roles. Returns 404 for a user who has not signed in to that Provider. The change is audited. Needs the admin manager tier.")
            .Produces<ProviderUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/settings", GetSettings)
            .WithName("AdminGetProviderRoleSettings")
            .WithSummary("A Provider's default role and whether its admins may give restricted roles")
            .WithDescription("A Provider with no setting returns the defaults: `viewer` and `allowAdminRestrictedGrants` false. Needs the admin manager tier.")
            .Produces<ProviderSettingsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/settings", SetSettings)
            .WithMetadata(new RequestSizeLimitMetadata(CustomerAccessLimits.MaxRequestBodyBytes))
            .WithName("AdminSetProviderRoleSettings")
            .WithSummary("Set a Provider's default role and restricted-grant switch")
            .WithDescription("Example body: `{\"defaultRoleKey\":\"analyst\",\"allowAdminRestrictedGrants\":true}`. The default must be an ordinary role that cannot manage users (`viewer` or `analyst`), or the answer is 409. It applies to users first recorded after the change. The change is audited. Needs the admin manager tier.")
            .Produces<ProviderSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static RoleActor? Actor(ClaimsPrincipal user)
    {
        if (!long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long id))
        {
            return null;
        }

        string name = user.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        return new RoleActor(id, name.Length > 200 ? name[..200] : name, true);
    }

    private static async Task<IResult> List(int providerId, int? page, ICustomerUserAdmin admin, CancellationToken cancellationToken) =>
        providerId < 1
            ? Results.NotFound()
            : Results.Ok(CustomerUserEndpoints.Page(await admin.ListAsync(providerId, page ?? 0, cancellationToken), 0, true));

    private static async Task<IResult> SetRole(int providerId, long userId, RoleRequest? body, HttpContext http, ICustomerUserAdmin admin, CancellationToken cancellationToken) =>
        providerId < 1 || Actor(http.User) is not { } actor
            ? Results.NotFound()
            : CustomerUserEndpoints.Respond(await admin.SetRoleAsync(actor, providerId, userId, body?.RoleKey ?? "", cancellationToken));

    private static async Task<IResult> GetSettings(int providerId, ICustomerUserAdmin admin, CancellationToken cancellationToken)
    {
        ProviderRoleSettings settings = await admin.GetSettingsAsync(providerId, cancellationToken);
        return Results.Ok(new ProviderSettingsResponse(settings.DefaultRoleKey, settings.AllowAdminRestrictedGrants));
    }

    private static async Task<IResult> SetSettings(int providerId, SettingsRequest? body, HttpContext http, ICustomerUserAdmin admin, CancellationToken cancellationToken)
    {
        if (providerId < 1 || Actor(http.User) is not { } actor)
        {
            return Results.NotFound();
        }

        if (body?.DefaultRoleKey is not { } key || body.AllowAdminRestrictedGrants is not { } allow)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["invalid_settings"] });
        }

        RoleChangeResult result = await admin.SetSettingsAsync(actor, providerId, new ProviderRoleSettings(key, allow), cancellationToken);
        return result.Status == RoleChangeStatus.Changed
            ? Results.Ok(new ProviderSettingsResponse(key, allow))
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The default role must be Viewer or Analyst.");
    }
}

public sealed record SettingsRequest(string? DefaultRoleKey, bool? AllowAdminRestrictedGrants);

public sealed record ProviderSettingsResponse(string DefaultRoleKey, bool AllowAdminRestrictedGrants);
