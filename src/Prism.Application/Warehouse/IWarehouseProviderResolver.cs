namespace Prism.Application.Warehouse;

/// <summary>
/// Turns the Provider id the user was authorized for into the Provider id the warehouse uses. They are different numberings: the Account Portal's
/// company id is <c>ods.company.integration_id</c> in the warehouse, and the warehouse's own Provider id is <c>ods.provider.id</c>. Metric Catalog queries
/// call this, so no endpoint can pass an unmapped id to a query, and a portal id that happens to equal another Provider's warehouse id never reads that Provider.
/// </summary>
public interface IWarehouseProviderResolver
{
    /// <summary>The id no warehouse Provider has. Queries given it return nothing.</summary>
    public const int Unmapped = 0;

    /// <summary>The warehouse Provider id for a portal company id, or <see cref="Unmapped"/> when there is none (or more than one, which is never guessed).</summary>
    Task<int> ResolveAsync(int portalCompanyId, CancellationToken cancellationToken);
}
