using Prism.Application.Warehouse;

namespace Prism.Tests.Integration.Metrics;

/// <summary>Stands in for the portal-to-warehouse Provider mapping: the id is unchanged unless the test maps it elsewhere.</summary>
public sealed class FakeProviderResolver(IReadOnlyDictionary<int, int>? map = null) : IWarehouseProviderResolver
{
    public Task<int> ResolveAsync(int portalCompanyId, CancellationToken cancellationToken) =>
        Task.FromResult(map is not null && map.TryGetValue(portalCompanyId, out int mapped) ? mapped : portalCompanyId);
}
