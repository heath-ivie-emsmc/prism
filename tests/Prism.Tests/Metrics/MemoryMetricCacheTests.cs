using Microsoft.Extensions.Logging;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Testing.Common;

namespace Prism.Tests.Metrics;

/// <summary>The aggregate Metric cache: keyed on Provider, Metric, parameters and Data Through, bounded in size, lifetime and concurrency.</summary>
public sealed class MemoryMetricCacheTests : IDisposable
{
    private static readonly DataThroughSource[] Trips = [DataThroughSource.TripDetail];

    private readonly StubDataThrough loads = new();
    private readonly TestTimeProvider clock = new(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
    private readonly ListLogger logger = new();
    private readonly MemoryMetricCache cache;
    private int computes;

    public MemoryMetricCacheTests() => cache = new MemoryMetricCache(loads, clock, logger);

    public void Dispose() => cache.Dispose();

    private Task<Box> AskAsync(int provider = 100, string key = "payer_mix", string parameters = "202610|Air|", string? value = null) =>
        cache.GetOrAddAsync(
            provider,
            key,
            parameters,
            Trips,
            _ =>
            {
                Interlocked.Increment(ref computes);
                return Task.FromResult(new Box(value ?? $"{provider}:{key}:{parameters}"));
            },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_second_identical_request_is_a_hit_and_computes_nothing()
    {
        Box first = await AskAsync();
        Box second = await AskAsync();

        Assert.Same(first, second);
        Assert.Equal(1, computes);
    }

    [Fact]
    public async Task A_different_metric_or_parameter_is_a_miss()
    {
        await AskAsync();
        await AskAsync(parameters: "202610|Ground|");
        await AskAsync(key: "payment_age");

        Assert.Equal(3, computes);
    }

    [Fact]
    public async Task A_new_load_date_busts_the_entry_and_the_old_one_is_never_served_again()
    {
        Box old = await AskAsync(value: "old");
        loads.Date = new DateOnly(2026, 11, 22);
        Box fresh = await AskAsync(value: "new");

        Assert.Equal("old", old.Text);
        Assert.Equal("new", fresh.Text);
        Assert.Equal(2, computes);
    }

    [Fact]
    public async Task Another_provider_never_reads_an_entry_cached_for_this_one_even_with_the_same_parameters()
    {
        Box mine = await AskAsync(provider: 100);
        Box theirs = await AskAsync(provider: 200);

        Assert.NotSame(mine, theirs);
        Assert.Equal("100:payer_mix:202610|Air|", mine.Text);
        Assert.Equal("200:payer_mix:202610|Air|", theirs.Text);
        Assert.Equal(2, computes);
    }

    [Fact]
    public async Task A_provider_with_no_data_is_computed_every_time_and_nothing_is_kept()
    {
        loads.Date = null;

        await AskAsync();
        await AskAsync();

        Assert.Equal(2, computes);
    }

    [Fact]
    public async Task An_entry_lives_at_most_ten_minutes()
    {
        await AskAsync();
        clock.Advance(TimeSpan.FromMinutes(9));
        await AskAsync();
        Assert.Equal(1, computes);

        clock.Advance(TimeSpan.FromMinutes(2));
        await AskAsync();
        Assert.Equal(2, computes);
    }

    [Fact]
    public async Task A_failed_computation_is_not_kept()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync<Box>(100, "payer_mix", "p", Trips, _ => throw new InvalidOperationException(), ct));

        Box ok = await AskAsync(parameters: "p");

        Assert.Equal("100:payer_mix:p", ok.Text);
    }

    [Fact]
    public async Task The_cache_holds_a_bounded_number_of_entries()
    {
        const int Distinct = MemoryMetricCache.CacheSize + 500;
        for (int i = 0; i < Distinct; i++)
        {
            await AskAsync(parameters: i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        computes = 0;
        for (int i = 0; i < Distinct; i++)
        {
            await AskAsync(parameters: i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Everything fits in the cap on neither pass, so some were recomputed, and the cap did not stop it caching altogether.
        Assert.InRange(computes, Distinct - MemoryMetricCache.CacheSize, Distinct - 1);
    }

    [Fact]
    public async Task Only_a_few_answers_are_computed_at_once()
    {
        int active = 0;
        int most = 0;
        Task[] asks = [.. Enumerable.Range(0, 40).Select(i => cache.GetOrAddAsync(
            100,
            "payer_mix",
            i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Trips,
            async ct =>
            {
                int now = Interlocked.Increment(ref active);
                int seen;
                do
                {
                    seen = Volatile.Read(ref most);
                }
                while (now > seen && Interlocked.CompareExchange(ref most, now, seen) != seen);

                await Task.Delay(10, ct);
                Interlocked.Decrement(ref active);
                return new Box("x");
            },
            TestContext.Current.CancellationToken))];

        await Task.WhenAll(asks);

        Assert.InRange(most, 1, MemoryMetricCache.MaxConcurrent);
    }

    private Task<Box> HeldAsync(int provider, string parameters, TaskCompletionSource release, CancellationToken cancellationToken, Action? started = null) =>
        cache.GetOrAddAsync(
            provider,
            "payer_mix",
            parameters,
            Trips,
            async ct =>
            {
                started?.Invoke();
                await release.Task.WaitAsync(ct);
                return new Box(parameters);
            },
            cancellationToken);

    private static async Task SettleAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task A_provider_using_all_of_its_slots_does_not_block_another_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        Task<Box>[] tenantA = [.. Enumerable.Range(0, MemoryMetricCache.MaxPerProvider + 3).Select(i => HeldAsync(100, $"a{i}", release, ct, () => Interlocked.Increment(ref running)))];
        await SettleAsync(() => Volatile.Read(ref running) == MemoryMetricCache.MaxPerProvider);

        Box other = await AskAsync(provider: 200, parameters: "b").WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Equal("200:payer_mix:b", other.Text);
        Assert.Equal(MemoryMetricCache.MaxPerProvider, Volatile.Read(ref running));
        Assert.All(tenantA, t => Assert.False(t.IsCompleted));

        release.SetResult();
        await Task.WhenAll(tenantA);
        Assert.Equal(MemoryMetricCache.MaxPerProvider + 3, Volatile.Read(ref running));
    }

    [Fact]
    public async Task A_provider_waiting_for_its_own_slot_holds_no_global_slot()
    {
        var ct = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        // Far more waiters than the global ceiling, all for one Provider: if waiting took a global slot, the other Provider could never start.
        Task<Box>[] tenantA = [.. Enumerable.Range(0, MemoryMetricCache.MaxConcurrent * 3).Select(i => HeldAsync(100, $"a{i}", release, ct, () => Interlocked.Increment(ref running)))];
        await SettleAsync(() => Volatile.Read(ref running) == MemoryMetricCache.MaxPerProvider);

        Box other = await AskAsync(provider: 300, parameters: "c").WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Equal("300:payer_mix:c", other.Text);
        release.SetResult();
        await Task.WhenAll(tenantA);
    }

    [Fact]
    public async Task The_global_ceiling_holds_across_providers()
    {
        var ct = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        Task<Box>[] asks = [.. Enumerable.Range(0, 20).SelectMany(p => Enumerable.Range(0, MemoryMetricCache.MaxPerProvider).Select(i => HeldAsync(1000 + p, $"p{i}", release, ct, () => Interlocked.Increment(ref running))))];
        await SettleAsync(() => Volatile.Read(ref running) == MemoryMetricCache.MaxConcurrent);
        await Task.Delay(100, ct);

        Assert.Equal(MemoryMetricCache.MaxConcurrent, Volatile.Read(ref running));

        release.SetResult();
        await Task.WhenAll(asks);
        Assert.Equal(40, Volatile.Read(ref running));
    }

    [Fact]
    public async Task A_request_waiting_for_a_slot_stops_when_cancelled_and_gives_nothing_away()
    {
        var ct = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        Task<Box>[] holders = [.. Enumerable.Range(0, MemoryMetricCache.MaxPerProvider).Select(i => HeldAsync(100, $"h{i}", release, ct, () => Interlocked.Increment(ref running)))];
        await SettleAsync(() => Volatile.Read(ref running) == MemoryMetricCache.MaxPerProvider);

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<Box> waiter = HeldAsync(100, "waiter", release, cancel.Token);
        await Task.Delay(50, ct);
        Assert.False(waiter.IsCompleted);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        release.SetResult();
        await Task.WhenAll(holders);
        // Both slots are free again: two new requests for the same Provider run together.
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int again = 0;
        Task<Box>[] next = [.. Enumerable.Range(0, MemoryMetricCache.MaxPerProvider).Select(i => HeldAsync(100, $"n{i}", second, ct, () => Interlocked.Increment(ref again)))];
        await SettleAsync(() => Volatile.Read(ref again) == MemoryMetricCache.MaxPerProvider);
        second.SetResult();
        await Task.WhenAll(next);
        Assert.Equal(0, cache.TrackedProviders);
    }

    [Fact]
    public async Task The_table_of_provider_gates_is_bounded_by_the_requests_in_flight()
    {
        var ct = TestContext.Current.CancellationToken;
        for (int provider = 1; provider <= 500; provider++)
        {
            await AskAsync(provider: provider, parameters: "x");
        }

        Assert.Equal(0, cache.TrackedProviders);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        Task<Box>[] inFlight = [.. Enumerable.Range(2000, 3).Select(p => HeldAsync(p, "y", release, ct, () => Interlocked.Increment(ref running)))];
        await SettleAsync(() => Volatile.Read(ref running) == 3);
        Assert.Equal(3, cache.TrackedProviders);

        release.SetResult();
        await Task.WhenAll(inFlight);
        Assert.Equal(0, cache.TrackedProviders);
    }

    [Fact]
    public async Task Concurrent_identical_requests_compute_once()
    {
        Task<Box>[] asks = [.. Enumerable.Range(0, 20).Select(_ => AskAsync())];

        await Task.WhenAll(asks);

        Assert.Equal(1, computes);
    }

    [Fact]
    public async Task Logs_name_the_metric_and_never_a_parameter_value_or_provider()
    {
        const string Marker = "ZZ-PHI-MARKER-9137";
        await AskAsync(parameters: Marker);
        await AskAsync(parameters: Marker);

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, m =>
        {
            Assert.DoesNotContain(Marker, m, StringComparison.Ordinal);
            Assert.DoesNotContain("100", m, StringComparison.Ordinal);
            Assert.Contains("payer_mix", m, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_claim_level_service_is_wrapped_by_a_caching_decorator()
    {
        Type[] decorators = [.. typeof(MemoryMetricCache).Assembly.GetTypes().Where(t => t.Name.StartsWith("Cached", StringComparison.Ordinal) && t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(Prism.Application.Metrics.IMetricCache))))];

        Assert.NotEmpty(decorators);
        Assert.DoesNotContain(decorators, t => typeof(Prism.Application.Attention.IClaimAttention).IsAssignableFrom(t));
    }

    [Fact]
    public async Task The_division_is_part_of_the_key_so_each_division_and_no_division_is_its_own_entry()
    {
        var metric = new CachedPayerMixMetric(new DivisionPayerMix(() => Interlocked.Increment(ref computes)), cache);
        var rotor = new ReportingDivision("rotor", "Rotor");
        var fixedWing = new ReportingDivision("fixed", "Fixed");
        var ct = TestContext.Current.CancellationToken;
        var period = new Prism.Application.Metrics.ApPeriod(2026, 10);
        var line = Prism.Application.Metrics.ServiceLineFilter.Air;

        PayerMix all = await metric.GetPayerMixAsync(100, period, line, null, null, ct);
        PayerMix rotorMix = await metric.GetPayerMixAsync(100, period, line, null, rotor, ct);
        PayerMix fixedMix = await metric.GetPayerMixAsync(100, period, line, null, fixedWing, ct);
        PayerMix rotorAgain = await metric.GetPayerMixAsync(100, period, line, null, rotor, ct);

        Assert.Equal(3, computes);
        Assert.Same(rotorMix, rotorAgain);
        Assert.Equal([null, "rotor", "fixed"], new[] { all, rotorMix, fixedMix }.Select(m => m.GroupName));
    }

    [Fact]
    public async Task Net_cash_keys_the_division_too_so_a_division_never_gets_the_whole_providers_series()
    {
        var metric = new CachedNetCollectionsMetric(new DivisionSeries(() => Interlocked.Increment(ref computes)), cache);
        var rotor = new ReportingDivision("rotor", "Rotor");
        var ct = TestContext.Current.CancellationToken;
        var period = new Prism.Application.Metrics.ApPeriod(2026, 10);
        var line = Prism.Application.Metrics.ServiceLineFilter.Both;

        NetCollectionsSeries all = await metric.GetSeriesAsync(100, period, 6, line, null, ct);
        NetCollectionsSeries rotorSeries = await metric.GetSeriesAsync(100, period, 6, line, rotor, ct);
        NetCollectionsSeries rotorAgain = await metric.GetSeriesAsync(100, period, 6, line, rotor, ct);
        NetCollectionsSeries allAgain = await metric.GetSeriesAsync(100, period, 6, line, null, ct);

        Assert.Equal(2, computes);
        Assert.Same(rotorSeries, rotorAgain);
        Assert.Same(all, allAgain);
        Assert.NotSame(all, rotorSeries);
    }

    private sealed class DivisionSeries(Action onCall) : INetCollectionsMetric
    {
        public Task<IReadOnlyList<Prism.Application.Metrics.ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Prism.Application.Metrics.ApPeriod>>([]);

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, Prism.Application.Metrics.ApPeriod endPeriod, int months, Prism.Application.Metrics.ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
        {
            onCall();
            return Task.FromResult(new NetCollectionsSeries(INetCollectionsMetric.Definition, endPeriod, months, null, []));
        }
    }

    private sealed class DivisionPayerMix(Action onCall) : IPayerMixMetric
    {
        public Task<PayerMix> GetPayerMixAsync(int providerId, Prism.Application.Metrics.ApPeriod period, Prism.Application.Metrics.ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            onCall();
            return Task.FromResult(new PayerMix(IPayerMixMetric.Definition, period, period.AddMonths(-1), serviceLine, groupKey, division?.Key, []));
        }
    }

    private sealed record Box(string Text);

    private sealed class StubDataThrough : IDataThroughReader
    {
        public DateOnly? Date { get; set; } = new DateOnly(2026, 11, 21);

        public Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken) => Task.FromResult(Date);
    }

    private sealed class ListLogger : ILogger<MemoryMetricCache>
    {
        private readonly List<string> messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (messages)
                {
                    return [.. messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (messages)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}
