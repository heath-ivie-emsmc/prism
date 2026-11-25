using Microsoft.Extensions.Logging.Abstractions;
using Prism.Application.Admin;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Admin;
using Prism.Testing.Common;

namespace Prism.Tests.Admin;

public sealed class ProviderCatalogWarmerTests
{
    private readonly TestTimeProvider clock = new(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));

    private sealed class StubSource(Func<IReadOnlyList<ProviderChoice>> providers) : IProviderListSource
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<ProviderChoice>> ListAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(providers());
        }
    }

    private (ProviderCatalogWarmer Warmer, ProviderCatalogCache Cache) Create(IProviderListSource source)
    {
        var cache = new ProviderCatalogCache(clock);
        return (new ProviderCatalogWarmer(source, cache, clock, NullLogger<ProviderCatalogWarmer>.Instance), cache);
    }

    [Fact]
    public async Task A_refresh_fills_the_cache_so_the_first_search_does_not_read_the_portal()
    {
        var directory = new StubSource(() => [new ProviderChoice("1", "Mercy Air")]);
        (ProviderCatalogWarmer warmer, ProviderCatalogCache cache) = Create(directory);

        TimeSpan wait = await warmer.RefreshOnceAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<ProviderChoice> found = await new ProviderCatalog(directory, cache).SearchAsync("mercy", TestContext.Current.CancellationToken);

        Assert.Equal(ProviderCatalogWarmer.Every, wait);
        Assert.Equal(["Mercy Air"], found.Select(p => p.Name));
        Assert.Equal(1, directory.Reads);
    }

    [Fact]
    public async Task A_failed_refresh_is_swallowed_and_tried_again_sooner()
    {
        var directory = new StubSource(() => throw new WarehouseUnavailableException());
        (ProviderCatalogWarmer warmer, _) = Create(directory);

        TimeSpan wait = await warmer.RefreshOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderCatalogWarmer.RetryAfterFailure, wait);
    }

    [Fact]
    public async Task Test_accounts_starting_with_zzz_are_never_offered_in_any_case_or_with_leading_space()
    {
        var directory = new StubSource(() => [new ProviderChoice("1", "zzzAbbeville County"), new ProviderChoice("2", "  ZZZ Demo"), new ProviderChoice("3", "Abbeville County Fire")]);
        var cache = new ProviderCatalogCache(clock);

        IReadOnlyList<ProviderChoice> found = await new ProviderCatalog(directory, cache).SearchAsync("abbeville", TestContext.Current.CancellationToken);
        IReadOnlyList<ProviderChoice> byName = await new ProviderCatalog(directory, cache).SearchAsync("zzz", TestContext.Current.CancellationToken);

        Assert.Equal(["Abbeville County Fire"], found.Select(p => p.Name));
        Assert.Empty(byName);
    }
}
