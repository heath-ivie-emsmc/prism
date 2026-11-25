using System.Globalization;
using System.Security.Claims;
using Prism.Api.SavedViews;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Customers;

/// <summary>
/// A Provider admin managing the roles of their own Provider's users. The Provider is the route's and must be one the portal assigned to the caller; the acting
/// user is the session's. A role, a permission or a user named in a body for anyone else is never read, and a user cannot change their own role.
/// </summary>
internal static class CustomerUserEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/users")
            .RequireAuthorization(Permissions.UsersManage)
            .WithTags("Users");

        group.MapGet("/", List)
            .WithName("ListProviderUsers")
            .WithSummary("The users of the caller's Provider, with their roles")
            .WithDescription(
                "One page of 50 users who have signed in to this Provider, by name, with the role each holds. `roles` is the catalog, and `assignable` says whether the caller may give that role: "
                + "a role with claim or PHI access (`restricted`) is assignable only when EMS staff have allowed it for this Provider. `page` starts at 0. Needs the users-manage permission. "
                + "Returns 404 for a Provider the caller is not assigned to.")
            .Produces<ProviderUsersResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{userId:long}/role", SetRole)
            .WithMetadata(new RequestSizeLimitMetadata(CustomerAccessLimits.MaxRequestBodyBytes))
            .WithName("SetProviderUserRole")
            .WithSummary("Change one user's role in the caller's Provider")
            .WithDescription(
                "Example body: `{\"roleKey\":\"analyst\"}`. Returns 409 for the caller's own role, for a restricted role the Provider does not allow, and for removing the last user who can manage users. "
                + "Returns 400 for a role that is not in the catalog and 404 for a user who has not signed in to this Provider. The change is audited.")
            .Produces<ProviderUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> List(string providerId, int? page, HttpContext http, IUserScopeProvider scope, ICustomerUserAdmin admin, CancellationToken cancellationToken)
    {
        if (await AssignedProviderAsync(http.User, providerId, scope, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ProviderUserPage result = await admin.ListAsync(id, page ?? 0, cancellationToken);
        bool allowRestricted = await admin.AllowsAdminRestrictedGrantsAsync(id, cancellationToken);
        long.TryParse(http.User.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long me);
        return Results.Ok(Page(result, me, allowRestricted));
    }

    private static async Task<IResult> SetRole(string providerId, long userId, RoleRequest? body, HttpContext http, IUserScopeProvider scope, ICustomerUserAdmin admin, CancellationToken cancellationToken)
    {
        if (await AssignedProviderAsync(http.User, providerId, scope, cancellationToken) is not { } id
            || !long.TryParse(http.User.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long me))
        {
            return Results.NotFound();
        }

        string name = http.User.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        var actor = new RoleActor(me, name.Length > 200 ? name[..200] : name, false);
        return Respond(await admin.SetRoleAsync(actor, id, userId, body?.RoleKey ?? "", cancellationToken));
    }

    /// <summary>The Provider as a number, only if the portal assigned it to this user. Facility limits do not matter here: roles are per Provider.</summary>
    internal static async Task<int?> AssignedProviderAsync(ClaimsPrincipal user, string providerId, IUserScopeProvider scope, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId)
            || !int.TryParse(providerId, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            return null;
        }

        try
        {
            IReadOnlyList<ProviderScope> providers = await scope.GetProvidersAsync(portalUserId, cancellationToken);
            return providers.Any(p => string.Equals(p.Id, providerId, StringComparison.Ordinal)) ? id : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static IResult Respond(RoleChangeResult result) => result.Status switch
    {
        RoleChangeStatus.Changed => Results.Ok(User(result.User!, 0)),
        RoleChangeStatus.UnknownRole => Results.ValidationProblem(new Dictionary<string, string[]> { ["roleKey"] = ["unknown_role"] }),
        RoleChangeStatus.OwnRole => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot change your own role."),
        RoleChangeStatus.RestrictedRole => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only EMS staff can give that role for this Provider."),
        RoleChangeStatus.LastAdmin => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This is the last user who can manage users for this Provider."),
        RoleChangeStatus.NotAllowed => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That is not allowed."),
        _ => Results.NotFound(),
    };

    internal static ProviderUsersResponse Page(ProviderUserPage page, long me, bool allowRestricted) => new(
        [.. page.Users.Select(u => User(u, me))],
        [.. CustomerRoles.All.Select(r => new RoleResponse(r.Key, r.Name, r.Description, r.Restricted, !r.Restricted || allowRestricted))],
        page.HasMore);

    private static ProviderUserResponse User(ProviderUserView u, long me) => new(u.PortalUserId, u.DisplayName, u.RoleKey, u.LastSignIn, u.PortalUserId == me);
}

/// <summary>The role to give. Only <c>roleKey</c> is read: a user, a Provider, a permission or a switch in the body is ignored.</summary>
public sealed record RoleRequest(string? RoleKey);

public sealed record ProviderUserResponse(long PortalUserId, string DisplayName, string RoleKey, DateTimeOffset LastSignIn, bool IsYou);

public sealed record RoleResponse(string Key, string Name, string Description, bool Restricted, bool Assignable);

public sealed record ProviderUsersResponse(IReadOnlyList<ProviderUserResponse> Users, IReadOnlyList<RoleResponse> Roles, bool HasMore);
