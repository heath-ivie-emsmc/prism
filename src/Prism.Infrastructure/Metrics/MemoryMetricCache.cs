using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// An in-memory <see cref="IMetricCache"/>. The cache has a size cap (one unit per answer) and a lifetime ceiling of ten minutes, judged by the injected clock,
/// and only a few answers are computed at once, because each one reads the warehouse: at most <see cref="MaxPerProvider"/> per Provider and <see cref="MaxConcurrent"/> overall
/// (see <see cref="ProviderGate"/>), so one Provider cannot use every slot while others wait. The key holds the Provider id the caller was authorized for, so an entry
/// is only ever found by a request for the same Provider, and the route's access check has already run before any Metric is asked. Logs say the Metric key only.
/// </summary>
public sealed partial class MemoryMetricCache(IDataThroughReader dataThrough, TimeProvider time, ILogger<MemoryMetricCache> logger) : IMetricCache, IDisposable
{
    internal const int CacheSize = 1000;
    internal const int MaxConcurrent = 8;
    internal const int MaxPerProvider = 2;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = CacheSize });
    private readonly ProviderGate gate = new(MaxConcurrent, MaxPerProvider);

    internal int TrackedProviders => gate.TrackedProviders;

    public async Task<T> GetOrAddAsync<T>(
        int providerId,
        string metricKey,
        string parameters,
        IReadOnlyList<DataThroughSource> sources,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken cancellationToken)
        where T : class
    {
        if (await dataThrough.GetAsync(providerId, sources, cancellationToken) is not { } loaded)
        {
            return await compute(cancellationToken);
        }

        var key = new Key(providerId, metricKey, parameters, loaded);
        if (Find<T>(key) is { } hit)
        {
            LogHit(metricKey);
            return hit;
        }

        using IDisposable slot = await gate.EnterAsync(providerId, cancellationToken);
        if (Find<T>(key) is { } raced)
        {
            LogHit(metricKey);
            return raced;
        }

        LogMiss(metricKey);
        T value = await compute(cancellationToken);
        // The clock decides when an answer is stale. The cache's own lifetime is only a ceiling that lets old entries be dropped.
        cache.Set(key, new Entry(value, time.GetUtcNow() + Lifetime), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime * 2 });
        return value;
    }

    public void Dispose()
    {
        cache.Dispose();
        gate.Dispose();
    }

    private T? Find<T>(Key key)
        where T : class =>
        cache.TryGetValue(key, out Entry? entry) && entry is { Value: T typed } && entry.ExpiresAt > time.GetUtcNow() ? typed : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metric cache hit for {MetricKey}")]
    private partial void LogHit(string metricKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metric cache miss for {MetricKey}")]
    private partial void LogMiss(string metricKey);

    private readonly record struct Key(int ProviderId, string MetricKey, string Parameters, DateOnly DataThrough);

    private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
}
