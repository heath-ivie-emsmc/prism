namespace Prism.Application.Warehouse;

/// <summary>
/// The Facilities of a Provider as the Account Portal numbers them: one per reporting division. A user whose assigned Facilities include all of them can read
/// the whole Provider. An empty answer (no divisions known) never grants full access.
/// </summary>
public interface IProviderDivisionDirectory
{
    /// <summary>The portal Facility ids of every reporting division of the Provider with this portal company id.</summary>
    Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken);
}
