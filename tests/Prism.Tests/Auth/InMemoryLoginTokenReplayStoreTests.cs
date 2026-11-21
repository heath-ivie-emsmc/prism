using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

public sealed class InMemoryLoginTokenReplayStoreTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly TestTimeProvider clock = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Accepts_an_id_once_and_refuses_the_second_use()
    {
        var store = new InMemoryLoginTokenReplayStore(clock);

        Assert.True(await store.TryRememberAsync("a", Window, TestContext.Current.CancellationToken));
        Assert.False(await store.TryRememberAsync("a", Window, TestContext.Current.CancellationToken));
        Assert.True(await store.TryRememberAsync("b", Window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Forgets_an_id_after_its_window()
    {
        var store = new InMemoryLoginTokenReplayStore(clock);
        await store.TryRememberAsync("a", Window, TestContext.Current.CancellationToken);

        clock.Advance(Window + TimeSpan.FromSeconds(1));

        Assert.True(await store.TryRememberAsync("a", Window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fails_closed_when_full_of_live_entries()
    {
        var store = new InMemoryLoginTokenReplayStore(clock);
        for (int i = 0; i < InMemoryLoginTokenReplayStore.MaxEntries; i++)
        {
            await store.TryRememberAsync($"id-{i}", Window, TestContext.Current.CancellationToken);
        }

        Assert.False(await store.TryRememberAsync("one-more", Window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Makes_room_by_sweeping_expired_entries_when_full()
    {
        var store = new InMemoryLoginTokenReplayStore(clock);
        for (int i = 0; i < InMemoryLoginTokenReplayStore.MaxEntries; i++)
        {
            await store.TryRememberAsync($"id-{i}", Window, TestContext.Current.CancellationToken);
        }

        clock.Advance(Window + TimeSpan.FromSeconds(1));

        Assert.True(await store.TryRememberAsync("fresh", Window, TestContext.Current.CancellationToken));
    }
}
