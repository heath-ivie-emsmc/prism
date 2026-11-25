using Prism.Infrastructure.Customers;
using Prism.Testing.Common;

namespace Prism.Tests.Customers;

public sealed class CustomerRoleCacheTests
{
    private readonly TestTimeProvider clock = new(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
    private static readonly IReadOnlySet<string> Held = new HashSet<string> { "prism.dashboard.view" };

    [Fact]
    public void An_entry_is_served_until_it_expires()
    {
        var cache = new CustomerRoleCache(clock);
        cache.Set(1, 10, Held);

        Assert.True(cache.TryGet(1, 10, out IReadOnlySet<string> found));
        Assert.Equal(Held, found);

        clock.Advance(CustomerRoleCache.LifeTime + TimeSpan.FromSeconds(1));

        Assert.False(cache.TryGet(1, 10, out _));
    }

    [Fact]
    public void Forget_clears_the_provider_and_the_any_provider_entry_for_that_user_only()
    {
        var cache = new CustomerRoleCache(clock);
        cache.Set(1, 10, Held);
        cache.Set(CustomerRoleCache.AnyProvider, 10, Held);
        cache.Set(1, 11, Held);

        cache.Forget(1, 10);

        Assert.False(cache.TryGet(1, 10, out _));
        Assert.False(cache.TryGet(CustomerRoleCache.AnyProvider, 10, out _));
        Assert.True(cache.TryGet(1, 11, out _));
    }

    [Fact]
    public void The_cache_stops_growing_at_its_cap_and_makes_room_from_expired_entries()
    {
        var cache = new CustomerRoleCache(clock);
        for (int i = 0; i < CustomerRoleCache.MaxEntries; i++)
        {
            cache.Set(1, i, Held);
        }

        cache.Set(1, CustomerRoleCache.MaxEntries, Held);
        Assert.False(cache.TryGet(1, CustomerRoleCache.MaxEntries, out _));

        clock.Advance(CustomerRoleCache.LifeTime + TimeSpan.FromSeconds(1));
        cache.Set(1, CustomerRoleCache.MaxEntries, Held);

        Assert.True(cache.TryGet(1, CustomerRoleCache.MaxEntries, out _));
    }
}
