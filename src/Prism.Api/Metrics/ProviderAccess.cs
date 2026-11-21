using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Metrics;

/// <summary>
/// Decides whether the signed-in user may read a Provider's data. The Provider comes from the route and is checked against the user's
/// assignments, read from the portal, on every request. A Provider the user is not assigned to and one that does not exist look the same: not found.
/// </summary>
internal sealed class ProviderAccess(IUserScopeProvider scope, IProviderDivisionDirectory divisions)
{
    /// <summary>
    /// The portal id of the Provider (as a number) if the user may read its aggregate figures, otherwise null.
    /// A Facility-limited user gets null too: Provider-level figures include Facilities they were not given, and a broad figure never widens a narrow assignment.
    /// A user whose Facilities include every reporting division of the Provider is not limited: they were given all of it.
    /// </summary>
    public async Task<int?> ResolveAsync(ClaimsPrincipal user, string providerId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), out long portalUserId))
        {
            return null;
        }

        IReadOnlyList<ProviderScope> providers;
        try
        {
            providers = await scope.GetProvidersAsync(portalUserId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Scope that cannot be read means no scope. Fail closed.
            return null;
        }

        ProviderScope? match = providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.Ordinal));
        if (match is null || !int.TryParse(match.Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
        {
            return null;
        }

        return match.FacilityIds.Count == 0 || await CoversEveryDivisionAsync(id, match.FacilityIds, cancellationToken) ? id : null;
    }

    // An unknown or empty division list never counts as covered, so a gap in the data fails closed.
    private async Task<bool> CoversEveryDivisionAsync(int portalCompanyId, IReadOnlyList<string> assigned, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> all = await divisions.GetFacilityIdsAsync(portalCompanyId, cancellationToken);
        return all.Count > 0 && all.IsSubsetOf(assigned);
    }
}
