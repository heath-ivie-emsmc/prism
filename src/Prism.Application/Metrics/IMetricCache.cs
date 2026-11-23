using Prism.Application.Warehouse;

namespace Prism.Application.Metrics;

/// <summary>
/// Keeps the answer of an aggregate Metric until the Provider's next warehouse load. The key is the Provider, the Metric, its normalized parameters and the
/// Data Through date, so a new load is never answered from old figures and one Provider never reads another's entry. Claim-level reads are never cached.
/// </summary>
public interface IMetricCache
{
    /// <summary>
    /// The cached answer for the key, or <paramref name="compute"/>'s result, which is then kept. Without a Data Through date (the Provider has no data) nothing is kept.
    /// A failed computation is not kept. <paramref name="parameters"/> is built by the caller from values already validated, and is never logged.
    /// </summary>
    Task<T> GetOrAddAsync<T>(
        int providerId,
        string metricKey,
        string parameters,
        IReadOnlyList<DataThroughSource> sources,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken cancellationToken)
        where T : class;
}
