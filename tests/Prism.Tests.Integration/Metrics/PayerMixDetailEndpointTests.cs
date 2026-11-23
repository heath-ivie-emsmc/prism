using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The payer mix drill: the links on a payer mix share, and the payer detail route they lead to (scope, refusals, bounds, no values in logs).</summary>
[Collection(LogCaptureGroup.Name)]
public sealed class PayerMixDetailEndpointTests : IDisposable
{
    private const string Series = "/api/v1/providers/100/metrics/net-collections";
    private const string Marker = "ZZ-MARKER-ZZ";

    private readonly FakeMix mix = new();
    private readonly FakeDetail detail = new();
    private readonly FakeDivisionDirectory divisions = new();
    private readonly AuthTestFactory factory;

    public PayerMixDetailEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<IPayerMixMetric>();
            services.AddSingleton<IPayerMixMetric>(mix);
            services.RemoveAll<IPayerMixDetailMetric>();
            services.AddSingleton<IPayerMixDetailMetric>(detail);
            services.RemoveAll<INetCollectionsMetric>();
            services.AddSingleton<INetCollectionsMetric>(new FakePeriods());
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(divisions);
            services.RemoveAll<IDataThroughReader>();
            services.AddSingleton<IDataThroughReader>(new FakeDataThrough());
        });
        // Provider 100 in full, 200 limited to one Facility (a sibling of the other), 300 read in full with no divisions of its own.
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"]), new ProviderScope("300", "All divisions", ["9", "10"])]);
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

    private static JsonElement Share(JsonElement body, string key) => body.GetProperty("shares").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);

    [Theory]
    [InlineData("commercial")]
    [InlineData("medicare")]
    [InlineData("va")]
    public async Task A_payer_group_with_detail_has_one_drill_link_to_its_payers(string key)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payer-mix?period=202610&serviceLine=air&group=fixed-wing&division=rotor-base");

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement link = Share(body, key).GetProperty("links").EnumerateArray().Single();
        Assert.Equal("drill", link.GetProperty("rel").GetString());
        Assert.Equal($"{Series}/payer-mix/{key}?group=fixed-wing&period=202610&serviceLine=air&division=rotor-base", link.GetProperty("href").GetString());
        Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("title").GetString()));
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("facility")]
    [InlineData("not-recorded")]
    public async Task A_group_without_payer_detail_has_an_empty_links_list(string key)
    {
        (_, JsonElement body) = await GetAsync($"{Series}/payer-mix?period=202610");

        JsonElement links = Share(body, key).GetProperty("links");
        Assert.Equal(JsonValueKind.Array, links.ValueKind);
        Assert.Equal(0, links.GetArrayLength());
    }

    [Fact]
    public async Task Links_omit_filters_that_are_not_in_effect_and_always_name_the_route_provider()
    {
        (_, JsonElement body) = await GetAsync($"/api/v1/providers/300/metrics/net-collections/payer-mix?period=202610");

        string href = Share(body, "commercial").GetProperty("links")[0].GetProperty("href").GetString()!;
        Assert.Equal("/api/v1/providers/300/metrics/net-collections/payer-mix/commercial?period=202610&serviceLine=air", href);
    }

    [Fact]
    public async Task A_user_without_the_dashboard_permission_gets_no_links_because_they_get_no_payer_mix()
    {
        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/payer-mix?period=202610", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain("links", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Following_a_link_returns_the_payers_of_that_group_for_the_same_filters_and_they_add_up_to_the_share()
    {
        (_, JsonElement mixBody) = await GetAsync($"{Series}/payer-mix?period=202610&serviceLine=air&group=fixed-wing&division=rotor-base");
        JsonElement commercial = Share(mixBody, "commercial");
        string href = commercial.GetProperty("links")[0].GetProperty("href").GetString()!;

        (HttpStatusCode status, JsonElement body) = await GetAsync(href);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("payer_mix_detail", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("100", "fixed-wing", "Fixed wing", "202610", "202609", "air"), (body.GetProperty("providerId").GetString(), body.GetProperty("group").GetString(), body.GetProperty("groupName").GetString(), body.GetProperty("period").GetString(), body.GetProperty("priorPeriod").GetString(), body.GetProperty("serviceLine").GetString()));
        Assert.Equal(("commercial", "Commercial"), (body.GetProperty("payerGroup").GetProperty("key").GetString(), body.GetProperty("payerGroup").GetProperty("name").GetString()));
        Assert.Equal(("2026-11-21", "rotor-base", "Mercy Flights - Rotor"), (body.GetProperty("dataThrough").GetString(), body.GetProperty("division").GetString(), body.GetProperty("divisionName").GetString()));
        JsonElement[] payers = [.. body.GetProperty("payers").EnumerateArray()];
        Assert.Equal(["PAYER ONE", "PAYER TWO", "Other"], payers.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(commercial.GetProperty("current").GetInt32(), payers.Sum(p => p.GetProperty("current").GetInt32()));
        Assert.Equal(commercial.GetProperty("prior").GetInt32(), payers.Sum(p => p.GetProperty("prior").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing", "commercial"), detail.LastCall);
        Assert.Equal("Mercy Flights - Rotor", detail.LastDivision?.Name);
    }

    [Fact]
    public async Task Without_parameters_the_detail_uses_the_newest_period_air_and_every_division()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payer-mix/medicare");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air, (string?)null, "medicare"), detail.LastCall);
        Assert.Null(detail.LastDivision);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("division").ValueKind);
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("facility")]
    [InlineData("not-recorded")]
    [InlineData("nope")]
    [InlineData("Commercial")]
    [InlineData("commercial%20")]
    [InlineData("ZZ-MARKER-ZZ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("commercial%27%3B%20DROP%20TABLE%20x")]
    public async Task A_group_without_detail_or_an_unknown_garbage_or_oversize_key_is_a_404_that_echoes_nothing_and_asks_nothing(string key)
    {
        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/payer-mix/{key}");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.DoesNotContain(key.Length > 40 ? key[..40] : key, text, StringComparison.Ordinal);
        Assert.Equal(0, detail.Calls);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/payer-mix/commercial")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/payer-mix/commercial")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/payer-mix/commercial")]
    public async Task Another_providers_detail_or_a_providers_the_user_is_limited_inside_is_a_404_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, detail.Calls);
    }

    [Theory]
    [InlineData("200")]
    [InlineData("999")]
    public async Task A_facility_limited_user_is_refused_every_division_including_a_sibling_and_the_directory_is_never_asked(string provider)
    {
        // The user of Provider 200 holds one Facility. A Facility-limited user never reads Provider-level figures, so the sibling division is the same 404 as their own.
        (HttpStatusCode own, _) = await GetAsync($"/api/v1/providers/{provider}/metrics/net-collections/payer-mix/commercial?division=rotor-base");
        (HttpStatusCode sibling, _) = await GetAsync($"/api/v1/providers/{provider}/metrics/net-collections/payer-mix/commercial?division=fixed-base");

        Assert.Equal(HttpStatusCode.NotFound, own);
        Assert.Equal(HttpStatusCode.NotFound, sibling);
        Assert.Equal(0, detail.Calls);
        Assert.Empty(divisions.Lookups);
    }

    [Fact]
    public async Task A_provider_cannot_use_another_providers_division_key()
    {
        (HttpStatusCode status, _) = await GetAsync("/api/v1/providers/300/metrics/net-collections/payer-mix/commercial?division=rotor-base");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, detail.Calls);
    }

    [Fact]
    public async Task The_detail_needs_the_dashboard_permission_and_a_session()
    {
        (HttpStatusCode limited, _) = await GetAsync($"{Series}/payer-mix/commercial", Permissions.App);
        using CookieClient anonymous = factory.NewClient();
        using HttpResponseMessage unauthenticated = await anonymous.GetAsync($"{Series}/payer-mix/commercial");

        Assert.Equal(HttpStatusCode.Forbidden, limited);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(0, detail.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    [InlineData("group=Fixed%20Wing")]
    [InlineData("group=fixed-wing%27%3B%20DROP%20TABLE%20x")]
    [InlineData("group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("division=ZZ-MARKER-ZZ")]
    [InlineData("division=Mercy%20Flights%20-%20Rotor")]
    [InlineData("division=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Bad_parameters_are_a_400_problem_that_echoes_no_value_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/payer-mix/commercial?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("errors", text, StringComparison.Ordinal);
        string value = query[(query.IndexOf('=', StringComparison.Ordinal) + 1)..];
        if (value.Length >= 8)
        {
            Assert.DoesNotContain(value.Length > 30 ? value[..30] : value, text, StringComparison.Ordinal);
        }

        Assert.Equal(0, detail.Calls);
    }

    [Fact]
    public async Task The_detail_holds_counts_and_payer_names_only()
    {
        (_, JsonElement body) = await GetAsync($"{Series}/payer-mix/commercial?period=202610");

        string[] properties = [.. body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["dataThrough", "division", "divisionName", "group", "groupName", "metric", "payerGroup", "payers", "period", "priorPeriod", "providerId", "serviceLine"], properties);
        Assert.All(body.GetProperty("payers").EnumerateArray(), p => Assert.Equal(["current", "name", "prior"], p.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        detail.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/payer-mix/commercial?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neither_a_payer_name_nor_a_rejected_value_reaches_the_logs()
    {
        detail.PayerName = Marker + "-PAYER";

        (HttpStatusCode ok, string okText) = await GetRawAsync($"{Series}/payer-mix/commercial?period=202610");
        (HttpStatusCode bad, _) = await GetRawAsync($"{Series}/payer-mix/commercial?period={Marker}&group={Marker}&division={Marker}");
        (HttpStatusCode refused, _) = await GetRawAsync($"{Series}/payer-mix/garbage?period={Marker}");
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Logs carry the request path (a code and a length, not values), never the query string or a payer name.
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Contains(Marker + "-PAYER", okText, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal(HttpStatusCode.NotFound, refused);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Marker, StringComparison.Ordinal));
    }

    private sealed class FakeDataThrough : IDataThroughReader
    {
        public Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken) =>
            Task.FromResult<DateOnly?>(new DateOnly(2026, 11, 21));
    }

    private sealed class FakePeriods : INetCollectionsMetric
    {
        public Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApPeriod>>([new(2026, 11), new(2026, 10)]);

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMix : IPayerMixMetric
    {
        public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Task.FromResult(new PayerMix(
                IPayerMixMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                groupKey is null ? null : "Fixed wing",
                [
                    new PayerMixShare("commercial", "Commercial", 10, 6),
                    new PayerMixShare("medicare", "Medicare", 4, 3),
                    new PayerMixShare("va", "VA", 2, 1),
                    new PayerMixShare("facility", "Facility", 1, 1),
                    new PayerMixShare("patient", "Patient responsibility", 1, 0),
                    new PayerMixShare("not-recorded", "Not recorded", 1, 0),
                ]));
    }

    private sealed class FakeDetail : IPayerMixDetailMetric
    {
        public Exception? Failure { get; set; }

        public string PayerName { get; set; } = "PAYER ONE";

        public int Calls { get; private set; }

        public ReportingDivision? LastDivision { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group, string PayerGroup) LastCall { get; private set; }

        public Task<PayerMixDetail> GetDetailAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, string payerGroupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Calls++;
            LastDivision = division;
            LastCall = (providerId, period, serviceLine, groupKey, payerGroupKey);
            if (Failure is not null)
            {
                return Task.FromException<PayerMixDetail>(Failure);
            }

            // Adds up to the commercial share of FakeMix: 10 now and 6 before.
            return Task.FromResult(new PayerMixDetail(
                IPayerMixDetailMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                groupKey is null ? null : "Fixed wing",
                payerGroupKey,
                payerGroupKey == "commercial" ? "Commercial" : "Medicare",
                [new PayerMixDetailPayer(PayerName, 6, 4), new PayerMixDetailPayer("PAYER TWO", 3, 2), new PayerMixDetailPayer("Other", 1, 0)]));
        }
    }
}
