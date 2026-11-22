using Microsoft.Extensions.Caching.Memory;
using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Infrastructure.Insights;

/// <summary>
/// Computes the "what changed" Insight and keeps it for a short time. The cache key holds the Provider, the period, the Service Line, the group and the
/// Data Through date of the warehouse load, so a new load is never answered from the old figures. The cache has a size cap and a lifetime, and only a few
/// Insights are computed at once, because each one reads the warehouse several times.
/// </summary>
public sealed class CachedInsightService(InsightsEngine engine, INetCollectionsMetric netCollections) : IInsightService, IDisposable
{
    internal const int CacheSize = 500;
    internal const int MaxConcurrent = 4;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = CacheSize });
    private readonly SemaphoreSlim gate = new(MaxConcurrent, MaxConcurrent);

    public async Task<InsightResult?> GetWhatChangedAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        // Data Through comes from the Provider's whole-figure series, which is one small read.
        NetCollectionsSeries series = await netCollections.GetSeriesAsync(providerId, period, 1, ServiceLineFilter.Both, cancellationToken);
        var key = (providerId, period, serviceLine, groupKey, series.SnapshotDate);
        if (cache.TryGetValue(key, out Cached? hit) && hit is not null)
        {
            return hit.Result;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            Insight? insight = await engine.ExplainAsync(providerId, period, serviceLine, groupKey, cancellationToken);
            InsightResult? result = insight is null ? null : new InsightResult(insight, InsightText.Build(insight), series.SnapshotDate);
            cache.Set(key, new Cached(result), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        cache.Dispose();
        gate.Dispose();
    }

    /// <summary>Wraps a result so that "nothing to explain" is cached too.</summary>
    private sealed record Cached(InsightResult? Result);
}
