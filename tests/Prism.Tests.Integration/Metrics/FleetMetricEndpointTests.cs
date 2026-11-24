using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// The routes of the call type, fleet, payer effect and pickup Metrics: scope, refusals, bounds, shape and logs. The Metrics are replaced by fakes that count their calls, so a
/// refusal is proven to happen before any figure is read. Cross-Provider and Facility-sibling requests are in <see cref="DivisionScopeEndpointTests"/>, which reads the catalog.
/// </summary>
[Collection(LogCaptureGroup.Name)]
public sealed partial class FleetMetricEndpointTests : IDisposable
{
    private const string Series = "/api/v1/providers/100/metrics/net-collections";
    private const string Marker = "ZZ-MARKER-ZZ";

    private readonly FakeMetrics fakes = new();
    private readonly AuthTestFactory factory;

    public FleetMetricEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<ITransportMixByCallTypeMetric>();
            services.AddSingleton<ITransportMixByCallTypeMetric>(fakes);
            services.RemoveAll<IAircraftPerformanceMetric>();
            services.AddSingleton<IAircraftPerformanceMetric>(fakes);
            services.RemoveAll<IBaseAnalysisMetric>();
            services.AddSingleton<IBaseAnalysisMetric>(fakes);
            services.RemoveAll<ILoadedMilesByAircraftMetric>();
            services.AddSingleton<ILoadedMilesByAircraftMetric>(fakes);
            services.RemoveAll<IPayerMixFinancialEffectMetric>();
            services.AddSingleton<IPayerMixFinancialEffectMetric>(fakes);
            services.RemoveAll<ITransportsByPickupLocationMetric>();
            services.AddSingleton<ITransportsByPickupLocationMetric>(fakes);
            services.RemoveAll<ICashContributionByAircraftMetric>();
            services.AddSingleton<ICashContributionByAircraftMetric>(fakes);
            services.RemoveAll<ICashContributionByDivisionMetric>();
            services.AddSingleton<ICashContributionByDivisionMetric>(fakes);
            services.RemoveAll<ICashContributionByPayerMetric>();
            services.AddSingleton<ICashContributionByPayerMetric>(fakes);
            services.RemoveAll<INetCollectionsMetric>();
            services.AddSingleton<INetCollectionsMetric>(new FakePeriods());
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new FakeDivisionDirectory());
            services.RemoveAll<IDataThroughReader>();
            services.AddSingleton<IDataThroughReader>(new FakeDataThrough());
        });
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    // The route of each Metric, by catalog key, and whether it returns a trend (and so takes `months`).
    private static readonly (string Key, string Route, bool Trend)[] Routes =
    [
        ("transport_mix_by_call_type", "transport-mix-by-call-type", true),
        ("aircraft_performance", "aircraft-performance", false),
        ("base_analysis", "base-analysis", false),
        ("loaded_miles_by_aircraft", "loaded-miles-by-aircraft", true),
        ("payer_mix_financial_effect", "payer-mix-financial-effect", false),
        ("transports_by_pickup_location", "transports-by-pickup-location", false),
        ("cash_contribution_by_aircraft", "cash-contribution/aircraft", false),
        ("cash_contribution_by_division", "cash-contribution/division", false),
        ("cash_contribution_by_payer", "cash-contribution/payer", false),
    ];

    public static TheoryData<string, string, bool> AllRoutes()
    {
        var data = new TheoryData<string, string, bool>();
        foreach ((string key, string route, bool trend) in Routes)
        {
            data.Add(key, route, trend);
        }

        return data;
    }

    [Fact]
    public void Every_route_in_this_table_is_available_and_every_new_metric_has_one()
    {
        string[] mine = ["transport_mix_by_call_type", "aircraft_performance", "base_analysis", "loaded_miles_by_aircraft", "payer_mix_financial_effect", "transports_by_pickup_location", "cash_contribution_by_aircraft", "cash_contribution_by_division", "cash_contribution_by_payer"];

        Assert.All(Routes, r => Assert.Equal(MetricStatus.Available, MetricCatalog.Find(r.Key)!.Status));
        Assert.All(Routes, r => Assert.Equal(r.Trend, MetricCatalog.Find(r.Key)!.Dimensions.Contains(MetricDimensions.Months)));
        Assert.All(Routes, r => Assert.Contains(r.Key, mine));
    }

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

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task A_valid_request_is_a_200_with_the_envelope_and_a_revalidation_header(string key, string route, bool trend)
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync($"{Series}/{route}?period=202610&division=rotor-base");
        JsonElement body = JsonElement.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { NoCache: true, Private: true });
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(key, body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("100", "202610", "2026-11-21", "rotor-base", "Mercy Flights - Rotor"), (body.GetProperty("providerId").GetString(), body.GetProperty("period").GetString(), body.GetProperty("dataThrough").GetString(), body.GetProperty("division").GetString(), body.GetProperty("divisionName").GetString()));
        Assert.Equal(trend, body.TryGetProperty("months", out _));
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Without_parameters_the_newest_period_is_used_and_the_service_line_defaults(string key, string route, bool trend)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/{route}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("202611", body.GetProperty("period").GetString());
        Assert.Equal("air", body.GetProperty("serviceLine").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("division").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("group").ValueKind);
        Assert.Equal(trend ? 6 : 0, trend ? body.GetProperty("months").GetInt32() : 0);
        Assert.False(string.IsNullOrEmpty(key));
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Bad_parameters_are_a_400_problem_that_echoes_no_value_and_no_figure_is_read(string key, string route, bool trend)
    {
        string[] bad =
        [
            "period=2026-10", "period=202613", "serviceLine=1", "serviceLine=Air,Ground", "group=Fixed%20Wing", "group=fixed-wing%27%3B%20DROP%20TABLE%20x",
            "group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "division=ZZ-MARKER-ZZ", "division=Mercy%20Flights%20-%20Rotor",
            "division=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            .. trend ? new[] { "months=7", "months=0", "months=-6", "months=24" } : [],
        ];

        foreach (string query in bad)
        {
            (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/{route}?{query}");

            Assert.True(status == HttpStatusCode.BadRequest, $"{key}: {query} gave {(int)status}");
            Assert.Contains("errors", text, StringComparison.Ordinal);
            string value = query[(query.IndexOf('=', StringComparison.Ordinal) + 1)..];
            if (value.Length >= 8)
            {
                Assert.DoesNotContain(value.Length > 30 ? value[..30] : value, text, StringComparison.Ordinal);
            }
        }

        if (trend)
        {
            // A months value that is not an integer never reaches the handler at all.
            foreach (string query in new[] { "months=99999999999", "months=abc" })
            {
                (HttpStatusCode status, _) = await GetRawAsync($"{Series}/{route}?{query}");
                Assert.Equal(HttpStatusCode.BadRequest, status);
            }
        }

        Assert.Equal(0, fakes.TotalCalls);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task The_route_needs_a_session_and_the_dashboard_permission(string key, string route, bool trend)
    {
        (HttpStatusCode limited, _) = await GetRawAsync($"{Series}/{route}?period=202610", Permissions.App);
        using CookieClient anonymous = factory.NewClient();
        using HttpResponseMessage unauthenticated = await anonymous.GetAsync($"{Series}/{route}?period=202610");

        Assert.Equal(HttpStatusCode.Forbidden, limited);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(0, fakes.TotalCalls);
        Assert.False(string.IsNullOrEmpty(key) || trend && route.Length == 0);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task A_warehouse_outage_is_a_503_that_says_nothing_about_why(string key, string route, bool trend)
    {
        fakes.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, string text) = await GetRawAsync($"{Series}/{route}?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(key) || trend && route.Length == 0);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Neither_warehouse_text_nor_a_rejected_value_reaches_the_logs(string key, string route, bool trend)
    {
        fakes.Text = Marker + "-TEXT";

        (HttpStatusCode ok, string okText) = await GetRawAsync($"{Series}/{route}?period=202610");
        (HttpStatusCode bad, _) = await GetRawAsync($"{Series}/{route}?period={Marker}&group={Marker}&division={Marker}");
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Contains(Marker + "-TEXT", okText, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Marker, StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(key) || trend && route.Length == 0);
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

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
