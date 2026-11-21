using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// Reads a Provider's reporting divisions from <c>ods.provider_reporting_division</c> and <c>ods.location</c>. The portal's Facility id is the location's
/// <c>integration_id</c> (checked against a real sign-in: the five Facility ids of Mercy Flights equal the five divisions' integration ids).
/// The Provider is the warehouse one, translated from the portal id first. Cached briefly, with a size cap.
/// </summary>
public sealed class ProviderDivisionDirectory(IWarehouse warehouse, IWarehouseProviderResolver providers) : IProviderDivisionDirectory, IDisposable
{
    internal const string DivisionsSql =
        """
        SELECT l.integration_id
        FROM ods.provider_reporting_division d
        JOIN ods.location l ON l.id = d.location_id
        WHERE d.provider_id = @provider_id AND l.integration_id IS NOT NULL
        LIMIT 500
        """;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2000 });

    public async Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(portalCompanyId, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        int warehouseId = await providers.ResolveAsync(portalCompanyId, cancellationToken);
        if (warehouseId == IWarehouseProviderResolver.Unmapped)
        {
            return new HashSet<string>();
        }

        IReadOnlyList<long> rows = await warehouse.QueryAsync(
            new WarehouseQuery(DivisionsSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = warehouseId }),
            reader => reader.GetInt64(0),
            cancellationToken);
        IReadOnlySet<string> ids = rows.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        cache.Set(portalCompanyId, ids, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return ids;
    }

    public void Dispose() => cache.Dispose();
}
