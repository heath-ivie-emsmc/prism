using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The breakdown drill: the links on each group of the net cash breakdown, and the payer mix they lead to (same filters, Provider scope, permission).</summary>
public sealed class BreakdownDrillEndpointTests : IDisposable
{
    private const string Series = "/api/v1/providers/100/metrics/net-collections";

    private readonly FakeBreakdown breakdown = new();
    private readonly FakeMix mix = new();
    private readonly AuthTestFactory factory;

    public BreakdownDrillEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<INetCashBreakdownMetric>();
            services.AddSingleton<INetCashBreakdownMetric>(breakdown);
            services.RemoveAll<IPayerMixMetric>();
            services.AddSingleton<IPayerMixMetric>(mix);
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new FakeDivisionDirectory());
            services.RemoveAll<IDataThroughReader>();
            services.AddSingleton<IDataThroughReader>(new FakeDataThrough());
        });
        // Provider 100 in full, 200 limited to one Facility (a sibling of the other).
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    private async Task<(HttpStatusCode Status, string Text)> GetRawAsync(string url, params string[] permissions)
    {
        using CookieClient client = factory.NewSignedInClient(permissions);
        using HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string url, params string[] permissions)
    {
        (HttpStatusCode status, string text) = await GetRawAsync(url, permissions);
        return (status, text.Length > 0 ? JsonElement.Parse(text) : default);
    }

    [Fact]
    public async Task Each_group_has_one_drill_link_to_its_payer_mix_with_the_filters_in_effect()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610&serviceLine=air&division=rotor-base");

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement[] groups = [.. body.GetProperty("groups").EnumerateArray()];
        Assert.Equal(["rotor-wing", "fixed-wing"], groups.Select(g => g.GetProperty("key").GetString()));
        foreach (JsonElement group in groups)
        {
            string key = group.GetProperty("key").GetString()!;
            JsonElement link = group.GetProperty("links").EnumerateArray().Single();
            Assert.Equal("drill", link.GetProperty("rel").GetString());
            Assert.Equal($"{Series}/payer-mix?group={key}&period=202610&serviceLine=air&division=rotor-base", link.GetProperty("href").GetString());
            Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("title").GetString()));
        }
    }

    [Fact]
    public async Task Links_omit_filters_that_are_not_in_effect()
    {
        (_, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610&serviceLine=all");

        string href = body.GetProperty("groups")[0].GetProperty("links")[0].GetProperty("href").GetString()!;
        Assert.Equal($"{Series}/payer-mix?group=rotor-wing&period=202610&serviceLine=all", href);
    }

    [Fact]
    public async Task Following_a_breakdown_link_returns_the_payer_mix_of_that_group_for_the_same_filters()
    {
        (_, JsonElement breakdownBody) = await GetAsync($"{Series}/breakdown?period=202610&serviceLine=air&division=rotor-base");
        string href = breakdownBody.GetProperty("groups")[1].GetProperty("links")[0].GetProperty("href").GetString()!;

        (HttpStatusCode status, JsonElement body) = await GetAsync(href);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("payer_mix", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("100", "fixed-wing", "202610", "air", "rotor-base"), (body.GetProperty("providerId").GetString(), body.GetProperty("group").GetString(), body.GetProperty("period").GetString(), body.GetProperty("serviceLine").GetString(), body.GetProperty("division").GetString()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing"), mix.LastCall);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("200")]
    public async Task A_link_followed_for_a_provider_the_caller_may_not_read_in_full_is_a_404_and_asks_nothing(string provider)
    {
        (_, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610&serviceLine=air");
        string href = body.GetProperty("groups")[0].GetProperty("links")[0].GetProperty("href").GetString()!;
        string other = href.Replace("/providers/100/", $"/providers/{provider}/", StringComparison.Ordinal);

        (HttpStatusCode status, string text) = await GetRawAsync(other);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, mix.Calls);
        Assert.DoesNotContain("commercial", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_caller_without_the_dashboard_permission_gets_no_breakdown_and_so_no_links()
    {
        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/breakdown?period=202610", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain("links", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_group_key_the_payer_mix_would_refuse_gets_an_empty_links_list()
    {
        breakdown.GroupKey = "Odd Key/1";

        (_, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610");

        JsonElement links = body.GetProperty("groups")[0].GetProperty("links");
        Assert.Equal(JsonValueKind.Array, links.ValueKind);
        Assert.Equal(0, links.GetArrayLength());
    }

    private sealed class FakeDataThrough : IDataThroughReader
    {
        public Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken) =>
            Task.FromResult<DateOnly?>(new DateOnly(2026, 11, 21));
    }

    private sealed class FakeBreakdown : INetCashBreakdownMetric
    {
        public string? GroupKey { get; set; }

        public Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
        {
            var figures = new BreakdownFigures(500m, 10, 900m, 7, 3);
            NetCashBreakdownGroup[] groups = GroupKey is null
                ? [new("rotor-wing", "Rotor wing", true, figures, figures), new("fixed-wing", "Fixed wing", true, figures, figures)]
                : [new(GroupKey, "Odd", true, figures, figures)];
            return Task.FromResult(new NetCashBreakdown(INetCashBreakdownMetric.Definition, period, period.AddMonths(-1), serviceLine, false, groups, []));
        }
    }

    private sealed class FakeMix : IPayerMixMetric
    {
        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group) LastCall { get; private set; }

        public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine, groupKey);
            return Task.FromResult(new PayerMix(IPayerMixMetric.Definition, period, period.AddMonths(-1), serviceLine, groupKey, groupKey is null ? null : "Fixed wing", [new PayerMixShare("commercial", "Commercial", 10, 6)]));
        }
    }
}
