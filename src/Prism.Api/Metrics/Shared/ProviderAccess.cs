using System.Security.Claims;
using System.Globalization;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Metrics;

/// <summary>
/// Decides whether the signed-in user may read a Provider's data. The Provider comes from the route and is checked against the user's
/// assignments, read from the portal, on every request. A Provider the user is not assigned to and one that does not exist look the same: not found.
/// </summary>
internal sealed class ProviderAccess(IUserScopeProvider scope, IProviderDivisionDirectory divisions, IDataThroughReader dataThrough)
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

    /// <summary>
    /// The Data Through date of a Metric's figures as <c>yyyy-MM-dd</c>, or null when the Provider has no data in the table the Metric reads.
    /// Call it only after <see cref="ResolveAsync"/> returned the Provider id, so the lookup is already scoped to a Provider the caller may read.
    /// </summary>
    public async Task<string?> DataThroughAsync(int providerId, string metricKey, ServiceLineFilter serviceLine, CancellationToken cancellationToken, bool byDivision = false)
    {
        DateOnly? date = await dataThrough.GetAsync(providerId, MetricDataThrough.SourcesFor(metricKey, serviceLine, byDivision), cancellationToken);
        return date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks the <c>division</c> query value against the Reporting Divisions of the route's Provider. No value means every division. A value is only ever matched
    /// to a key the directory made from the Provider's own data, never to a display name, and its length and characters are checked before anything is looked up.
    /// Call it only after <see cref="ResolveAsync"/> returned the Provider id. A Facility-limited user never gets this far: they get 404 from <see cref="ResolveAsync"/>,
    /// which is stricter than limiting them to their own divisions.
    /// </summary>
    public async Task<DivisionSelection> ResolveDivisionAsync(int providerId, string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
        {
            return default;
        }

        if (key.Length > ReportingDivision.MaxKeyLength || !MetricEndpointSupport.DivisionKeyPattern().IsMatch(key))
        {
            return new DivisionSelection(null, "division must be a key from the divisions route, such as rotor-base.");
        }

        IReadOnlyList<ReportingDivision> known = await divisions.GetReportingDivisionsAsync(providerId, cancellationToken);
        ReportingDivision? match = known.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
        return match is null
            ? new DivisionSelection(null, "division is not a Reporting Division of this Provider. Use a key from the divisions route.")
            : new DivisionSelection(match, null);
    }

    // An unknown or empty division list never counts as covered, so a gap in the data fails closed.
    private async Task<bool> CoversEveryDivisionAsync(int portalCompanyId, IReadOnlyList<string> assigned, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> all = await divisions.GetFacilityIdsAsync(portalCompanyId, cancellationToken);
        return all.Count > 0 && all.IsSubsetOf(assigned);
    }
}

/// <summary>The outcome of checking a <c>division</c> value: the division, nothing (every division), or the reason it was refused.</summary>
internal readonly record struct DivisionSelection(ReportingDivision? Division, string? Error);
