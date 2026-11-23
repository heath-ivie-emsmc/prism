using System.Globalization;
using System.Text;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

// One small decorator per aggregate Metric interface (the interfaces differ in signature, so one cannot serve them all): it lists the values that make an answer
// different and leaves the key, the Data Through sources and the caching to MetricCacheCall. Claim-level reads (IClaimAttention) are not decorated and never reach the cache.

/// <summary>
/// The canonical text of the values that make one answer of a Metric differ from another. Each value is written as its length, a colon, its text and a semicolon
/// (<c>null</c> is a lone <c>~;</c>), so no choice of values can run into its neighbours or be mistaken for a different list. Values come from validated input
/// and are formatted with the invariant culture.
/// </summary>
internal static class MetricCacheKeys
{
    internal static string Of(params object?[] parts)
    {
        var text = new StringBuilder();
        foreach (object? part in parts)
        {
            if (part is null)
            {
                text.Append("~;");
                continue;
            }

            string value = part is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : part.ToString() ?? string.Empty;
            text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }

        return text.ToString();
    }
}

/// <summary>The one place a decorator turns a call into a cache lookup: the Metric's own key, the values that distinguish the answer, and the tables it reads.</summary>
internal static class MetricCacheCall
{
    internal static Task<T> Cached<T>(
        this IMetricCache cache,
        MetricDefinition metric,
        int providerId,
        ServiceLineFilter serviceLine,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken cancellationToken,
        params object?[] distinguishing)
        where T : class =>
        cache.GetOrAddAsync(providerId, metric.Key, MetricCacheKeys.Of(distinguishing), MetricDataThrough.SourcesFor(metric.Key, serviceLine), compute, cancellationToken);
}

/// <summary>Caches <see cref="INetCollectionsMetric"/> answers against the load date of the table they read.</summary>
public sealed class CachedNetCollectionsMetric(INetCollectionsMetric inner, IMetricCache cache) : INetCollectionsMetric
{
    public Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken) =>
        cache.GetOrAddAsync(providerId, INetCollectionsMetric.Definition.Key + "#periods", "", MetricDataThrough.SourcesFor(INetCollectionsMetric.Definition.Key, ServiceLineFilter.Both), ct => inner.GetPeriodsAsync(providerId, ct), cancellationToken);

    public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, CancellationToken cancellationToken) =>
        cache.Cached(INetCollectionsMetric.Definition, providerId, serviceLine, ct => inner.GetSeriesAsync(providerId, endPeriod, months, serviceLine, ct), cancellationToken, endPeriod, months, serviceLine);
}

/// <summary>Caches <see cref="INetCashProjectionMetric"/> answers.</summary>
public sealed class CachedNetCashProjectionMetric(INetCashProjectionMetric inner, IMetricCache cache) : INetCashProjectionMetric
{
    public Task<NetCashProjection> GetProjectionAsync(int providerId, ApPeriod afterPeriod, ServiceLineFilter serviceLine, CancellationToken cancellationToken) =>
        cache.Cached(INetCashProjectionMetric.Definition, providerId, serviceLine, ct => inner.GetProjectionAsync(providerId, afterPeriod, serviceLine, ct), cancellationToken, afterPeriod, serviceLine);
}

/// <summary>Caches <see cref="INetCashBreakdownMetric"/> answers.</summary>
public sealed class CachedNetCashBreakdownMetric(INetCashBreakdownMetric inner, IMetricCache cache) : INetCashBreakdownMetric
{
    public Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(INetCashBreakdownMetric.Definition, providerId, serviceLine, ct => inner.GetBreakdownAsync(providerId, period, serviceLine, division, ct), cancellationToken, period, serviceLine, division?.Key);
}

/// <summary>Caches <see cref="INetCashByGroupMetric"/> answers.</summary>
public sealed class CachedNetCashByGroupMetric(INetCashByGroupMetric inner, IMetricCache cache) : INetCashByGroupMetric
{
    public Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(INetCashByGroupMetric.Definition, providerId, serviceLine, ct => inner.GetNetCashByGroupAsync(providerId, period, serviceLine, division, ct), cancellationToken, period, serviceLine, division?.Key);
}

/// <summary>Caches <see cref="ICashPerTransportMetric"/> answers.</summary>
public sealed class CachedCashPerTransportMetric(ICashPerTransportMetric inner, IMetricCache cache) : ICashPerTransportMetric
{
    public Task<CashPerTransport> GetCashPerTransportAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(ICashPerTransportMetric.Definition, providerId, serviceLine, ct => inner.GetCashPerTransportAsync(providerId, period, serviceLine, groupKey, division, ct), cancellationToken, period, serviceLine, groupKey, division?.Key);
}

/// <summary>Caches <see cref="IPaymentAgeMetric"/> answers.</summary>
public sealed class CachedPaymentAgeMetric(IPaymentAgeMetric inner, IMetricCache cache) : IPaymentAgeMetric
{
    public Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(IPaymentAgeMetric.Definition, providerId, serviceLine, ct => inner.GetPaymentAgeAsync(providerId, period, serviceLine, groupKey, division, ct), cancellationToken, period, serviceLine, groupKey, division?.Key);
}

/// <summary>Caches <see cref="IPayerMixMetric"/> answers.</summary>
public sealed class CachedPayerMixMetric(IPayerMixMetric inner, IMetricCache cache) : IPayerMixMetric
{
    public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(IPayerMixMetric.Definition, providerId, serviceLine, ct => inner.GetPayerMixAsync(providerId, period, serviceLine, groupKey, division, ct), cancellationToken, period, serviceLine, groupKey, division?.Key);
}

/// <summary>Caches <see cref="ITransportVolumeMetric"/> answers.</summary>
public sealed class CachedTransportVolumeMetric(ITransportVolumeMetric inner, IMetricCache cache) : ITransportVolumeMetric
{
    public Task<TransportVolume> GetVolumeAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(ITransportVolumeMetric.Definition, providerId, serviceLine, ct => inner.GetVolumeAsync(providerId, endPeriod, months, serviceLine, division, ct), cancellationToken, endPeriod, months, serviceLine, division?.Key);
}

/// <summary>Caches <see cref="ICleanClaimRateMetric"/> answers.</summary>
public sealed class CachedCleanClaimRateMetric(ICleanClaimRateMetric inner, IMetricCache cache) : ICleanClaimRateMetric
{
    public Task<CleanClaimRate> GetRateAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken) =>
        cache.Cached(ICleanClaimRateMetric.Definition, providerId, ServiceLineFilter.Both, ct => inner.GetRateAsync(providerId, endPeriod, months, ct), cancellationToken, endPeriod, months);
}

/// <summary>Caches <see cref="IPayerMixDetailMetric"/> answers.</summary>
public sealed class CachedPayerMixDetailMetric(IPayerMixDetailMetric inner, IMetricCache cache) : IPayerMixDetailMetric
{
    public Task<PayerMixDetail> GetDetailAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, string payerGroupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
        cache.Cached(IPayerMixDetailMetric.Definition, providerId, serviceLine, ct => inner.GetDetailAsync(providerId, period, serviceLine, groupKey, payerGroupKey, division, ct), cancellationToken, period, serviceLine, groupKey, division?.Key, payerGroupKey);
}
