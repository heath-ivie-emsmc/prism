using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Auth.Identity;

/// <summary>The signed-in user: identity and permissions from the session, Providers from the portal.</summary>
internal static class MeEndpoint
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/me", Handle)
            .WithName("GetMe")
            .WithTags("Identity")
            .WithSummary("The signed-in user")
            .WithDescription("Returns the user, the Providers they may see (with how many Facilities each is limited to) and their permissions. Returns 401 when there is no valid session.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static async Task<IResult> Handle(ClaimsPrincipal user, IUserScopeProvider scope, CancellationToken cancellationToken)
    {
        string userName = user.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        string displayName = user.FindFirstValue(SessionTokenIssuer.DisplayNameClaim) ?? userName;
        string[] permissions = user.FindAll(SessionTokenIssuer.PermissionsClaim).Select(c => c.Value).ToArray();

        // The Providers are not in the session. They are read from the portal (and remembered for a few minutes), so a Provider that
        // was taken away stops showing up within minutes. If they cannot be read, the user has none rather than an old answer.
        IReadOnlyList<ProviderScope> providers = [];
        if (long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), out long portalUserId))
        {
            try
            {
                providers = await scope.GetProvidersAsync(portalUserId, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                providers = [];
            }
        }

        return Results.Ok(new MeResponse(
            userName,
            displayName,
            providers.Select(p => new ProviderResponse(p.Id, p.Name, p.FacilityIds.Count)).ToArray(),
            permissions));
    }
}

/// <summary>Response of <c>GET /api/me</c>.</summary>
public sealed record MeResponse(string UserName, string DisplayName, IReadOnlyList<ProviderResponse> Providers, IReadOnlyList<string> Permissions);

/// <summary>A Provider the user may see. <paramref name="FacilityCount"/> is how many Facilities they are limited to, and 0 means all of them. The Facility ids stay on the server.</summary>
public sealed record ProviderResponse(string Id, string Name, int FacilityCount);
