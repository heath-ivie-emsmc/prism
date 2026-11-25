using System.Security.Claims;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Application.Personas;
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

    private static string[] PermissionsIn(IReadOnlyDictionary<int, IReadOnlySet<string>> held, string providerId) =>
        int.TryParse(providerId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) && held.TryGetValue(id, out IReadOnlySet<string>? permissions)
            ? [.. Permissions.All.Select(p => p.Id).Where(permissions.Contains)]
            : [];

    /// <summary>Records the user and returns what they may do in each Provider. If the roles cannot be read the user holds nothing, never an old answer.</summary>
    private static async Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> HeldAsync(
        IReadOnlyList<ProviderScope> providers,
        ICustomerAccess customers,
        string displayName,
        long portalUserId,
        CancellationToken cancellationToken)
    {
        if (providers.Count == 0 || portalUserId == 0)
        {
            return new Dictionary<int, IReadOnlySet<string>>();
        }

        int[] ids = [.. providers.Select(p => int.TryParse(p.Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0).Where(id => id > 0)];
        try
        {
            return await customers.RecordAsync(portalUserId, displayName, ids, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
        {
            return new Dictionary<int, IReadOnlySet<string>>();
        }
    }

    public static async Task<IResult> Handle(ClaimsPrincipal user, IUserScopeProvider scope, ICustomerAccess customers, IOptions<PrismAuthOptions> authOptions, IOnboardingService onboarding, CancellationToken cancellationToken)
    {
        string userName = user.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        string displayName = user.FindFirstValue(SessionTokenIssuer.DisplayNameClaim) ?? userName;
        string[] permissions = user.FindAll(SessionTokenIssuer.PermissionsClaim).Select(c => c.Value).ToArray();

        // The Providers are not in the session. They are read from the portal (and remembered for a few minutes), so a Provider that
        // was taken away stops showing up within minutes. If they cannot be read, the user has none rather than an old answer.
        IReadOnlyList<ProviderScope> providers = [];
        // Not read for an Internal User: the portal's company list for the internal group is every company, which is not their customer scope.
        if (long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), out long portalUserId) && !user.HasClaim(SessionTokenIssuer.InternalClaim, "true"))
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

        // What the user may do in each Provider comes from their Customer Role, recorded here the first time they are seen. An Internal User has none.
        IReadOnlyDictionary<int, IReadOnlySet<string>> held = await HeldAsync(providers, customers, displayName, portalUserId, cancellationToken);

        // The server alone decides this. It is false when there is no portal user, no usable questionnaire, or the questionnaire cannot be read.
        bool onboardingRequired = held.Values.Any(p => p.Contains(Permissions.DashboardView))
            && portalUserId != 0
            && await onboarding.IsRequiredAsync(portalUserId, cancellationToken);

        return Results.Ok(new MeResponse(
            userName,
            displayName,
            providers.Select(p => new ProviderResponse(p.Id, p.Name, p.FacilityIds.Count) { Permissions = PermissionsIn(held, p.Id) }).ToArray(),
            permissions.Where(permission => !Permissions.AdminTiers.Contains(permission)).ToArray())
        {
            OnboardingRequired = onboardingRequired,
            IsInternal = user.HasClaim(SessionTokenIssuer.InternalClaim, "true"),
            AdminPermissions = user.HasClaim(SessionTokenIssuer.InternalClaim, "true")
                ? permissions.Where(Permissions.AdminTiers.Contains).ToArray()
                : [],
        });
    }
}

/// <summary>Response of <c>GET /api/me</c>.</summary>
public sealed record MeResponse(string UserName, string DisplayName, IReadOnlyList<ProviderResponse> Providers, IReadOnlyList<string> Permissions)
{
    /// <summary>True when the user has no Persona yet and a questionnaire is published. Set by the server only; the SPA sends the user to the questionnaire first.</summary>
    public bool OnboardingRequired { get; init; }

    /// <summary>True for a member of the internal portal group. It only decides whether the SPA shows the admin area; the server decides access.</summary>
    public bool IsInternal { get; init; }

    /// <summary>The admin tier permissions, for an Internal User only.</summary>
    public IReadOnlyList<string> AdminPermissions { get; init; } = [];
}

/// <summary>A Provider the user may see. <paramref name="FacilityCount"/> is how many Facilities they are limited to, and 0 means all of them. The Facility ids stay on the server.</summary>
public sealed record ProviderResponse(string Id, string Name, int FacilityCount)
{
    /// <summary>What the user may do in this Provider, from their Customer Role. The SPA uses it to show or hide things; the server checks every request again.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
