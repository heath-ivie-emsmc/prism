using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// Maps a portal company id to the warehouse Provider id through <c>ods.company.integration_id</c> and <c>ods.provider.integration_id</c>.
/// The answer is cached for a while (the mapping almost never changes) in a cache with a size cap, so a user cannot grow it without limit.
/// In Development, <c>Warehouse:DevelopmentProviderId</c> still wins when it is set.
/// </summary>
public sealed class WarehouseProviderResolver(IWarehouse warehouse, IOptions<PrismWarehouseOptions> options) : IWarehouseProviderResolver, IDisposable
{
    // The portal id is the one bound parameter, named like every other Provider parameter so the warehouse's Provider-filter rule applies here too.
    // A second row means the mapping is ambiguous, so it is treated as no mapping.
    internal const string ResolveSql =
        """
        SELECT p.id
        FROM ods.provider p
        JOIN ods.company c ON c.id = p.integration_id
        WHERE c.integration_id = @provider_id
        LIMIT 2
        """;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2000 });

    public async Task<int> ResolveAsync(int portalCompanyId, CancellationToken cancellationToken)
    {
        if (options.Value.DevelopmentProviderId is { } standIn)
        {
            return standIn;
        }

        if (cache.TryGetValue(portalCompanyId, out int cached))
        {
            return cached;
        }

        IReadOnlyList<int> rows = await warehouse.QueryAsync(
            new WarehouseQuery(ResolveSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = portalCompanyId }),
            reader => reader.GetInt32(0),
            cancellationToken);
        int resolved = rows.Count == 1 ? rows[0] : IWarehouseProviderResolver.Unmapped;
        cache.Set(portalCompanyId, resolved, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return resolved;
    }

    public void Dispose() => cache.Dispose();
}
