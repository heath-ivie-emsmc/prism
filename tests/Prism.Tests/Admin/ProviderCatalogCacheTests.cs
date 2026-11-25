using Prism.Application.Admin;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Admin;
using Prism.Testing.Common;

namespace Prism.Tests.Admin;

public sealed class ProviderCatalogCacheTests
{
    private readonly TestTimeProvider clock = new(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
    private static readonly IReadOnlyList<ProviderChoice> List = [new ProviderChoice("1", "One")];

    [Fact]
    public async Task The_list_is_read_once_until_it_expires()
    {
        var cache = new ProviderCatalogCache(clock);
        int loads = 0;
        Task<IReadOnlyList<ProviderChoice>> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(List);
        }

        await cache.GetAsync(Load, TestContext.Current.CancellationToken);
        await cache.GetAsync(Load, TestContext.Current.CancellationToken);
        Assert.Equal(1, loads);

        clock.Advance(ProviderCatalogCache.LifeTime + TimeSpan.FromSeconds(1));
        await cache.GetAsync(Load, TestContext.Current.CancellationToken);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_serving_the_old_list()
    {
        var cache = new ProviderCatalogCache(clock);
        await cache.GetAsync(_ => Task.FromResult(List), TestContext.Current.CancellationToken);
        clock.Advance(ProviderCatalogCache.LifeTime + TimeSpan.FromSeconds(1));

        IReadOnlyList<ProviderChoice> served = await cache.GetAsync(_ => throw new WarehouseUnavailableException(), TestContext.Current.CancellationToken);

        Assert.Equal(List, served);
    }

    [Fact]
    public async Task A_failure_with_no_old_list_is_reported()
    {
        var cache = new ProviderCatalogCache(clock);

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => cache.GetAsync(_ => throw new WarehouseUnavailableException(), TestContext.Current.CancellationToken));
    }
}
