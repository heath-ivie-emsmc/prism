using Microsoft.Extensions.DependencyInjection;
using Prism.Api.Health;
using Prism.Infrastructure.Persistence;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Persistence;

public sealed class ReadinessCheckTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_callers_share_one_database_check()
    {
        var probe = new CountingProbe { Gate = new TaskCompletionSource<bool>() };
        var check = new ReadinessCheck(probe, new TestTimeProvider(Start));

        Task<bool>[] callers = Enumerable.Range(0, 50).Select(_ => check.IsReadyAsync()).ToArray();
        probe.Gate.SetResult(true);
        bool[] results = await Task.WhenAll(callers);

        Assert.Equal(1, probe.Calls);
        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task The_result_is_reused_for_the_cache_window_then_checked_again()
    {
        var clock = new TestTimeProvider(Start);
        var probe = new CountingProbe();
        var check = new ReadinessCheck(probe, clock);

        await check.IsReadyAsync();
        clock.Advance(ReadinessCheck.CacheFor - TimeSpan.FromSeconds(1));
        await check.IsReadyAsync();
        Assert.Equal(1, probe.Calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        await check.IsReadyAsync();
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task A_down_database_is_reported_not_ready_and_recovery_is_noticed_after_the_window()
    {
        var clock = new TestTimeProvider(Start);
        var probe = new CountingProbe { Result = false };
        var check = new ReadinessCheck(probe, clock);

        Assert.False(await check.IsReadyAsync());

        probe.Result = true;
        clock.Advance(ReadinessCheck.CacheFor + TimeSpan.FromSeconds(1));
        Assert.True(await check.IsReadyAsync());
    }

    [Fact]
    public async Task A_check_that_hangs_is_cut_off_at_the_timeout()
    {
        var check = new ReadinessCheck(new HangingProbe(), TimeProvider.System);

        Assert.False(await check.IsReadyAsync());
    }

    [Fact]
    public async Task The_real_probe_connects_to_the_migrated_database()
    {
        await using var provider = ProbeServices(postgres.ConnectionString);

        Assert.True(await new PrismDatabaseProbe(provider.GetRequiredService<IServiceScopeFactory>())
            .IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_real_probe_reports_an_unreachable_database_without_throwing()
    {
        await using var provider = ProbeServices("Host=127.0.0.1;Port=1;Database=prism;Timeout=2;Command Timeout=2");

        Assert.False(await new PrismDatabaseProbe(provider.GetRequiredService<IServiceScopeFactory>())
            .IsAvailableAsync(TestContext.Current.CancellationToken));
    }

    private static ServiceProvider ProbeServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<PrismDbContext>(options => options.UsePrismNpgsql(Prism.Testing.Common.TestDatabaseGuard.Assert(connectionString)));
        return services.BuildServiceProvider();
    }

    private sealed class CountingProbe : IDatabaseProbe
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public bool Result { get; set; } = true;

        public TaskCompletionSource<bool>? Gate { get; init; }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Gate is null ? Result : await Gate.Task;
        }
    }

    private sealed class HangingProbe : IDatabaseProbe
    {
        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }
    }
}
