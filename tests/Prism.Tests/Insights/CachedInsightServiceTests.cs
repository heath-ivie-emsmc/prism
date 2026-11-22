using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Infrastructure.Insights;

namespace Prism.Tests.Insights;

public sealed class CachedInsightServiceTests : IDisposable
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private readonly CountingCash cash = new();
    private readonly Loads loads = new();
    private readonly CachedInsightService service;

    public CachedInsightServiceTests() => service = new CachedInsightService(new InsightsEngine(cash, []), loads);

    public void Dispose() => service.Dispose();

    private Task<InsightResult?> AskAsync(string? group = "fixed-wing", int provider = 1) =>
        service.GetWhatChangedAsync(provider, Oct, ServiceLineFilter.Air, group, TestContext.Current.CancellationToken);

    private sealed class CountingCash : INetCashByGroupMetric
    {
        private int calls;
        private int active;
        private int mostActive;

        public int Calls => calls;

        public int MostActive => mostActive;

        public bool Empty { get; set; }

        public async Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            int now = Interlocked.Increment(ref active);
            int seen;
            do
            {
                seen = mostActive;
            }
            while (now > seen && Interlocked.CompareExchange(ref mostActive, now, seen) != seen);

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref active);
            NetCashByGroupPoint[] points = Empty
                ? []
                : [new("fixed-wing", "Fixed wing", true, period.AddMonths(-1), 1_000m, 1), new("fixed-wing", "Fixed wing", true, period, 400m, 1)];
            return new NetCashByGroup(INetCashByGroupMetric.Definition, period, INetCashByGroupMetric.MonthsBefore, serviceLine, points);
        }
    }

    private sealed class Loads : INetCollectionsMetric
    {
        public DateOnly? Snapshot { get; set; } = new DateOnly(2026, 11, 5);

        public Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApPeriod>>([]);

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, CancellationToken cancellationToken) =>
            Task.FromResult(new NetCollectionsSeries(INetCollectionsMetric.Definition, endPeriod, months, Snapshot, []));
    }

    [Fact]
    public async Task The_same_question_on_the_same_load_is_answered_once()
    {
        InsightResult? first = await AskAsync();
        InsightResult? second = await AskAsync();

        Assert.Same(first, second);
        Assert.Equal(1, cash.Calls);
        Assert.Equal(new DateOnly(2026, 11, 5), first!.DataThrough);
    }

    [Fact]
    public async Task A_new_warehouse_load_is_never_answered_from_the_old_figures()
    {
        await AskAsync();
        loads.Snapshot = new DateOnly(2026, 11, 6);

        InsightResult? after = await AskAsync();

        Assert.Equal(2, cash.Calls);
        Assert.Equal(new DateOnly(2026, 11, 6), after!.DataThrough);
    }

    [Fact]
    public async Task Another_provider_or_group_is_a_different_question()
    {
        await AskAsync();
        await AskAsync(provider: 2);
        await AskAsync(group: null);

        Assert.Equal(3, cash.Calls);
    }

    [Fact]
    public async Task Nothing_to_explain_is_kept_too()
    {
        cash.Empty = true;

        Assert.Null(await AskAsync());
        Assert.Null(await AskAsync());

        Assert.Equal(1, cash.Calls);
    }

    [Fact]
    public async Task Only_a_few_insights_are_computed_at_once()
    {
        await Task.WhenAll(Enumerable.Range(1, 12).Select(i => AskAsync(provider: i)));

        Assert.Equal(12, cash.Calls);
        Assert.InRange(cash.MostActive, 1, CachedInsightService.MaxConcurrent);
    }
}
