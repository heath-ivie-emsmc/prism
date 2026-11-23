using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// The Metric cache behind the real routes: the access check runs before the cache, an entry is only found by the Provider it was computed for,
/// a new load date busts it, and claim-level reads are never wrapped.
/// </summary>
public sealed class MetricCacheEndpointTests : IDisposable
{
    private readonly CountingPayerMix inner = new();
    private readonly MutableDataThrough load = new();
    private readonly AuthTestFactory factory;

    public MetricCacheEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<IDataThroughReader>();
            services.AddSingleton<IDataThroughReader>(load);
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new FakeDivisionDirectory());
            services.RemoveAll<IPayerMixMetric>();
            services.AddSingleton<IPayerMixMetric>(sp => new CachedPayerMixMetric(inner, sp.GetRequiredService<IMetricCache>()));
        });
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "A", []), new ProviderScope("101", "B", []), new ProviderScope("200", "Limited", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    private async Task<(HttpStatusCode Status, int? Current)> GetAsync(int provider)
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync($"/api/v1/providers/{provider}/metrics/net-collections/payer-mix?period=202610");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, null);
        }

        JsonElement body = JsonElement.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (response.StatusCode, body.GetProperty("shares")[0].GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task A_repeat_request_is_served_from_the_cache()
    {
        (_, int? first) = await GetAsync(100);
        (_, int? second) = await GetAsync(100);

        Assert.Equal(1000, first);
        Assert.Equal(first, second);
        Assert.Equal([100], inner.Providers);
    }

    [Fact]
    public async Task Two_providers_the_caller_may_read_never_share_an_entry()
    {
        (_, int? a) = await GetAsync(100);
        (_, int? b) = await GetAsync(101);
        (_, int? aAgain) = await GetAsync(100);

        Assert.Equal((1000, 1010, 1000), (a, b, aAgain));
        Assert.Equal([100, 101], inner.Providers);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(999)]
    public async Task A_provider_the_caller_may_not_read_is_a_404_even_when_another_provider_is_cached_and_the_cache_is_never_asked(int provider)
    {
        await GetAsync(100);
        await GetAsync(101);
        int before = inner.Providers.Count;

        (HttpStatusCode status, _) = await GetAsync(provider);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(before, inner.Providers.Count);
        Assert.DoesNotContain(provider, inner.Providers);
    }

    [Theory]
    [InlineData("metrics/net-collections?endPeriod=202610")]
    [InlineData("metrics/net-collections/payer-mix?period=202610")]
    [InlineData("metrics/net-collections/payer-mix/commercial?period=202610")]
    [InlineData("metrics/net-collections/breakdown?period=202610")]
    [InlineData("metrics/net-collections/by-group?period=202610")]
    [InlineData("metrics/net-collections/payment-age?period=202610")]
    [InlineData("metrics/net-collections/cash-per-transport?period=202610")]
    [InlineData("metrics/net-collections/transports?endPeriod=202610")]
    [InlineData("metrics/net-collections/clean-claims?endPeriod=202610")]
    [InlineData("metrics/net-collections/projection?endPeriod=202610")]
    [InlineData("metrics/net-collections/divisions")]
    [InlineData("insights/what-changed?period=202610")]
    public async Task A_caller_who_may_not_read_the_data_never_gets_a_304_even_with_a_wildcard_if_none_match(string path)
    {
        var headers = new Dictionary<string, string> { ["If-None-Match"] = "*" };
        foreach (string provider in new[] { "200", "999" })
        {
            using CookieClient client = factory.NewSignedInClient();
            using HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, $"/api/v1/providers/{provider}/{path}", headers: headers);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.ETag);
        }

        using CookieClient noPermission = factory.NewSignedInClient(Permissions.App);
        using HttpResponseMessage forbidden = await noPermission.SendAsync(HttpMethod.Get, $"/api/v1/providers/100/{path}", headers: headers);
        using CookieClient anonymous = factory.NewClient();
        using HttpResponseMessage unauthenticated = await anonymous.SendAsync(HttpMethod.Get, $"/api/v1/providers/100/{path}", headers: headers);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task A_new_load_date_is_computed_again()
    {
        await GetAsync(100);
        load.Date = new DateOnly(2026, 11, 22);
        await GetAsync(100);

        Assert.Equal([100, 100], inner.Providers);
    }

    [Fact]
    public void Every_aggregate_metric_is_wrapped_by_the_cache_and_claim_level_reads_are_not()
    {
        using var plain = new AuthTestFactory();
        IServiceProvider services = plain.Services;

        Assert.IsType<CachedNetCollectionsMetric>(services.GetRequiredService<INetCollectionsMetric>());
        Assert.IsType<CachedNetCashProjectionMetric>(services.GetRequiredService<INetCashProjectionMetric>());
        Assert.IsType<CachedNetCashBreakdownMetric>(services.GetRequiredService<INetCashBreakdownMetric>());
        Assert.IsType<CachedNetCashByGroupMetric>(services.GetRequiredService<INetCashByGroupMetric>());
        Assert.IsType<CachedCashPerTransportMetric>(services.GetRequiredService<ICashPerTransportMetric>());
        Assert.IsType<CachedPaymentAgeMetric>(services.GetRequiredService<IPaymentAgeMetric>());
        Assert.IsType<CachedPayerMixMetric>(services.GetRequiredService<IPayerMixMetric>());
        Assert.IsType<CachedTransportVolumeMetric>(services.GetRequiredService<ITransportVolumeMetric>());
        Assert.IsType<CachedCleanClaimRateMetric>(services.GetRequiredService<ICleanClaimRateMetric>());
        Assert.IsType<ClaimAttentionReader>(services.GetRequiredService<Prism.Application.Attention.IClaimAttention>());
    }

    private sealed class MutableDataThrough : IDataThroughReader
    {
        public DateOnly? Date { get; set; } = new DateOnly(2026, 11, 21);

        public Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken) => Task.FromResult(Date);
    }

    /// <summary>Answers with a figure that depends on the Provider, so an entry served to the wrong Provider shows.</summary>
    private sealed class CountingPayerMix : IPayerMixMetric
    {
        private readonly List<int> providers = [];

        public IReadOnlyList<int> Providers
        {
            get
            {
                lock (providers)
                {
                    return [.. providers];
                }
            }
        }

        public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            lock (providers)
            {
                providers.Add(providerId);
            }

            return Task.FromResult(new PayerMix(
                IPayerMixMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                null,
                [new PayerMixShare("commercial", "Commercial", 1000 + (providerId - 100) * 10, 0)]));
        }
    }
}
