using Prism.Application.Metrics;

namespace Prism.Application.Warehouse;

/// <summary>
/// The Facilities of a Provider as the Account Portal numbers them: one per reporting division. A user whose assigned Facilities include all of them can read
/// the whole Provider. An empty answer (no divisions known) never grants full access.
/// </summary>
public interface IProviderDivisionDirectory
{
    /// <summary>The portal Facility ids of every reporting division of the Provider with this portal company id.</summary>
    Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken);

    /// <summary>
    /// The Reporting Divisions the Provider's transports are filed under in the latest warehouse load, with the keys the <c>division</c> filter accepts.
    /// The Provider is the one in the route. A division is only ever found here, never made from a display name or a request value. An unmapped Provider has none.
    /// </summary>
    Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken);

    /// <summary>
    /// The same divisions, only those with at least one Transport under <paramref name="serviceLine"/> in the latest load. Keys are the ones <see cref="GetReportingDivisionsAsync(int, CancellationToken)"/>
    /// gives (never renumbered by the filter), so a key means the same thing under every Service Line. <see cref="ServiceLineFilter.Both"/> is the full list.
    /// The default returns the full list, for stand-ins that cannot tell the Service Line of a division.
    /// </summary>
    Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, ServiceLineFilter serviceLine, CancellationToken cancellationToken) =>
        GetReportingDivisionsAsync(portalCompanyId, cancellationToken);
}
