using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The net cash routes: who may call them, which Provider they may name, and what bad input and a down warehouse look like.</summary>
public sealed class NetCollectionsEndpointTests : IDisposable
{
    private const string Series = "/api/v1/providers/100/metrics/net-collections";

    private readonly FakeNetCollections metric = new();
    private readonly FakeProjection projection = new();
    private readonly FakeBreakdown breakdown = new();
    private readonly FakePayerMix payerMix = new();
    private readonly AuthTestFactory factory;

    public NetCollectionsEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<INetCollectionsMetric>();
            services.AddSingleton<INetCollectionsMetric>(metric);
            services.RemoveAll<INetCashProjectionMetric>();
            services.AddSingleton<INetCashProjectionMetric>(projection);
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new FakeDivisions());
            services.RemoveAll<INetCashBreakdownMetric>();
            services.AddSingleton<INetCashBreakdownMetric>(breakdown);
            services.RemoveAll<IPayerMixMetric>();
            services.AddSingleton<IPayerMixMetric>(payerMix);
        });
        // Provider 100 in full, 200 limited to one Facility, 300 not numeric.
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"]), new ProviderScope("abc", "Odd", []), new ProviderScope("300", "All divisions", ["9", "10", "11"]), new ProviderScope("400", "Unknown divisions", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string url, params string[] permissions)
    {
        using CookieClient client = factory.NewSignedInClient(permissions);
        using HttpResponseMessage response = await client.GetAsync(url);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.Length > 0 ? JsonElement.Parse(text) : default);
    }

    [Fact]
    public async Task A_user_with_access_gets_the_series_with_labels_and_the_metric_definition()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}?endPeriod=202610&months=3");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("net_collections", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal("USD", body.GetProperty("metric").GetProperty("unit").GetString());
        Assert.Equal("202610", body.GetProperty("endPeriod").GetString());
        Assert.Equal("2026-11-05", body.GetProperty("snapshotDate").GetString());
        JsonElement last = body.GetProperty("points").EnumerateArray().Last();
        Assert.Equal(("202610", "Oct", 2026), (last.GetProperty("period").GetString(), last.GetProperty("label").GetString(), last.GetProperty("year").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), 3), metric.LastSeriesCall);
    }

    [Fact]
    public async Task Without_parameters_it_uses_six_months_ending_at_the_newest_period()
    {
        await GetAsync(Series);

        Assert.Equal((100, new ApPeriod(2026, 11), 6), metric.LastSeriesCall);
    }

    [Fact]
    public async Task The_projection_returns_months_with_a_range_and_sums_for_the_chosen_period_and_service_line()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/projection?endPeriod=202610&serviceLine=air");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("net_cash_projection", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal("air", body.GetProperty("serviceLine").GetString());
        Assert.Equal(["202611", "202612", "202701"], body.GetProperty("months").EnumerateArray().Select(m => m.GetProperty("period").GetString()));
        Assert.Equal("Jan", body.GetProperty("months")[2].GetProperty("label").GetString());
        Assert.Equal(2027, body.GetProperty("months")[2].GetProperty("year").GetInt32());
        Assert.Equal(3600m, body.GetProperty("total").GetDecimal());
        Assert.Equal(3000m, body.GetProperty("low").GetDecimal());
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air), projection.LastCall);
    }

    [Fact]
    public async Task The_projection_defaults_to_all_service_lines_after_the_newest_period()
    {
        await GetAsync($"{Series}/projection");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Both), projection.LastCall);
    }

    [Theory]
    [InlineData("serviceLine=both")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=air,ground")]
    [InlineData("endPeriod=2026-10")]
    [InlineData("endPeriod=202613")]
    public async Task The_projection_refuses_bad_parameters_and_queries_nothing(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/projection?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, projection.Calls);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/projection")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/projection")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/projection")]
    public async Task The_projection_of_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, projection.Calls);
    }

    [Fact]
    public async Task The_projection_needs_the_dashboard_permission_and_a_signed_in_user()
    {
        (HttpStatusCode forbidden, _) = await GetAsync($"{Series}/projection", Permissions.App);
        using var anonymous = factory.NewClient();
        using HttpResponseMessage response = await anonymous.GetAsync($"{Series}/projection");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, projection.Calls);
    }

    [Fact]
    public async Task A_projection_when_the_warehouse_is_down_is_a_503_problem()
    {
        projection.Failure = new WarehouseUnavailableException(new InvalidOperationException("Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/projection?endPeriod=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_given_every_reporting_division_of_a_provider_may_read_it_even_though_facilities_are_listed()
    {
        (HttpStatusCode status, _) = await GetAsync("/api/v1/providers/300/metrics/net-collections/periods");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task A_user_with_only_some_divisions_or_a_provider_whose_divisions_are_unknown_stays_limited()
    {
        (HttpStatusCode some, _) = await GetAsync("/api/v1/providers/200/metrics/net-collections/periods");
        (HttpStatusCode unknown, _) = await GetAsync("/api/v1/providers/400/metrics/net-collections/periods");

        Assert.Equal(HttpStatusCode.NotFound, some);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(0, metric.Calls);
    }

    [Fact]
    public async Task The_breakdown_returns_groups_and_rows_for_the_chosen_period_and_service_line()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610&serviceLine=air");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("net_cash_breakdown", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("202610", "202609", "air"), (body.GetProperty("period").GetString(), body.GetProperty("priorPeriod").GetString(), body.GetProperty("serviceLine").GetString()));
        Assert.True(body.GetProperty("periodOpen").GetBoolean());
        JsonElement group = body.GetProperty("groups")[0];
        Assert.Equal("Rotor wing", group.GetProperty("name").GetString());
        Assert.Equal(500m, group.GetProperty("current").GetProperty("cash").GetDecimal());
        Assert.Equal(400m, group.GetProperty("prior").GetProperty("cash").GetDecimal());
        JsonElement row = body.GetProperty("rows")[0];
        Assert.Equal(("N119MF", "Rotor"), (row.GetProperty("vehicle").GetString(), row.GetProperty("base").GetString()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air), breakdown.LastCall);
    }

    [Fact]
    public async Task The_breakdown_defaults_to_air_after_the_newest_period()
    {
        await GetAsync($"{Series}/breakdown");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air), breakdown.LastCall);
    }

    [Theory]
    [InlineData("serviceLine=both")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=air,ground")]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("period=202610%27%3B%20DROP%20TABLE%20x")]
    public async Task The_breakdown_refuses_bad_parameters_and_queries_nothing(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/breakdown?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, breakdown.Calls);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/breakdown")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/breakdown")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/breakdown")]
    public async Task The_breakdown_of_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, breakdown.Calls);
    }

    [Fact]
    public async Task The_breakdown_needs_the_dashboard_permission_and_a_signed_in_user()
    {
        (HttpStatusCode forbidden, _) = await GetAsync($"{Series}/breakdown", Permissions.App);
        using var anonymous = factory.NewClient();
        using HttpResponseMessage response = await anonymous.GetAsync($"{Series}/breakdown");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, breakdown.Calls);
    }

    [Fact]
    public async Task A_breakdown_when_the_warehouse_is_down_is_a_503_problem()
    {
        breakdown.Failure = new WarehouseUnavailableException(new InvalidOperationException("Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/breakdown?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Periods_come_back_newest_first_with_display_labels()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/periods");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["202611", "202610"], body.GetProperty("periods").EnumerateArray().Select(p => p.GetProperty("period").GetString()));
        Assert.Equal("Nov 2026", body.GetProperty("periods")[0].GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections")]
    [InlineData("/api/v1/providers/999/metrics/net-collections/periods")]
    [InlineData("/api/v1/providers/200/metrics/net-collections")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/periods")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections")]
    [InlineData("/api/v1/providers/100%20OR%201=1/metrics/net-collections")]
    public async Task A_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, metric.Calls);
    }

    [Fact]
    public async Task A_user_without_the_dashboard_permission_is_forbidden_and_nothing_is_queried()
    {
        (HttpStatusCode status, _) = await GetAsync(Series, Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, metric.Calls);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using var client = factory.NewClient();

        using HttpResponseMessage response = await client.GetAsync(Series);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("months=0")]
    [InlineData("months=25")]
    [InlineData("months=-1")]
    [InlineData("endPeriod=2026-10")]
    [InlineData("endPeriod=202613")]
    [InlineData("endPeriod=202610%27%3B%20DROP%20TABLE%20x")]
    public async Task Bad_parameters_are_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, metric.SeriesCalls);
    }

    [Fact]
    public async Task A_provider_with_no_data_gets_an_empty_series_not_an_error()
    {
        metric.Periods.Clear();

        (HttpStatusCode status, JsonElement body) = await GetAsync(Series);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("points").EnumerateArray());
    }

    [Fact]
    public async Task A_warehouse_that_is_down_is_a_503_problem_that_says_nothing_about_why()
    {
        metric.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync(Series);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret.internal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperation", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_id_sent_in_a_header_or_body_cannot_change_which_provider_is_read()
    {
        using CookieClient client = factory.NewSignedInClient();

        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, $"{Series}?providerId=999", headers: new Dictionary<string, string> { ["X-Provider-Id"] = "999" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, metric.LastSeriesCall.ProviderId);
    }

    [Fact]
    public async Task Payer_mix_for_a_group_returns_the_shares_for_the_chosen_month_and_names_the_group()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payer-mix?period=202610&serviceLine=air&group=fixed-wing");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("payer_mix", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("202610", "202609", "fixed-wing", "Fixed wing"), (body.GetProperty("period").GetString(), body.GetProperty("priorPeriod").GetString(), body.GetProperty("group").GetString(), body.GetProperty("groupName").GetString()));
        JsonElement va = body.GetProperty("shares").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "va");
        Assert.Equal((5, 4), (va.GetProperty("current").GetInt32(), va.GetProperty("prior").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing"), payerMix.LastCall);
    }

    [Fact]
    public async Task Payer_mix_without_parameters_uses_the_newest_period_and_air()
    {
        await GetAsync($"{Series}/payer-mix");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air, (string?)null), payerMix.LastCall);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/payer-mix")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/payer-mix")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/payer-mix")]
    public async Task Payer_mix_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, payerMix.Calls);
    }

    [Fact]
    public async Task Payer_mix_needs_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/payer-mix", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, payerMix.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    [InlineData("group=Fixed%20Wing")]
    [InlineData("group=fixed-wing%27%3B%20DROP%20TABLE%20x")]
    [InlineData("group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Payer_mix_with_bad_parameters_is_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payer-mix?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, payerMix.Calls);
    }

    [Fact]
    public async Task Payer_mix_holds_counts_only_no_payer_or_patient_names()
    {
        (_, JsonElement body) = await GetAsync($"{Series}/payer-mix?period=202610");

        string text = body.GetRawText();
        Assert.DoesNotContain("AETNA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("primPay", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payer_mix_in_a_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        payerMix.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payer-mix?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class FakeNetCollections : INetCollectionsMetric
    {
        public List<ApPeriod> Periods { get; } = [new(2026, 11), new(2026, 10)];

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public int SeriesCalls { get; private set; }

        public (int ProviderId, ApPeriod End, int Months) LastSeriesCall { get; private set; }

        public Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null ? Task.FromResult<IReadOnlyList<ApPeriod>>([.. Periods]) : Task.FromException<IReadOnlyList<ApPeriod>>(Failure);
        }

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken)
        {
            Calls++;
            SeriesCalls++;
            LastSeriesCall = (providerId, endPeriod, months);
            if (Failure is not null)
            {
                return Task.FromException<NetCollectionsSeries>(Failure);
            }

            List<NetCollectionsPoint> points = Enumerable.Range(0, months).Select(i => new NetCollectionsPoint(endPeriod.AddMonths(-(months - 1 - i)), 1000m * (i + 1))).ToList();
            return Task.FromResult(new NetCollectionsSeries(INetCollectionsMetric.Definition, endPeriod, months, new DateOnly(2026, 11, 5), points));
        }
    }

    private sealed class FakeProjection : INetCashProjectionMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod After, ServiceLineFilter ServiceLine) LastCall { get; private set; }

        public Task<NetCashProjection> GetProjectionAsync(int providerId, ApPeriod afterPeriod, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, afterPeriod, serviceLine);
            if (Failure is not null)
            {
                return Task.FromException<NetCashProjection>(Failure);
            }

            List<NetCashProjectionMonth> months = Enumerable.Range(1, 3).Select(i => new NetCashProjectionMonth(afterPeriod.AddMonths(i), 1200m, 1000m, 1400m)).ToList();
            return Task.FromResult(new NetCashProjection(INetCashProjectionMetric.Definition, afterPeriod, serviceLine, new DateOnly(2026, 11, 5), months));
        }
    }

    private sealed class FakeDivisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId is 200 or 300 ? new HashSet<string> { "9", "10" } : []);
    }

    private sealed class FakeBreakdown : INetCashBreakdownMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine) LastCall { get; private set; }

        public Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine);
            if (Failure is not null)
            {
                return Task.FromException<NetCashBreakdown>(Failure);
            }

            var current = new BreakdownFigures(500m, 10, 900m, 7, 3);
            var prior = new BreakdownFigures(400m, 8, 700m, 5, 3);
            return Task.FromResult(new NetCashBreakdown(
                INetCashBreakdownMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                period.Month == 10,
                [new NetCashBreakdownGroup("rotor-wing", "Rotor wing", true, current, prior)],
                [new NetCashBreakdownRow("rotor-wing", "Rotor wing", true, "Rotor", "N119MF", current, prior)]));
        }
    }

    private sealed class FakePayerMix : IPayerMixMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group) LastCall { get; private set; }

        public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine, groupKey);
            if (Failure is not null)
            {
                return Task.FromException<PayerMix>(Failure);
            }

            return Task.FromResult(new PayerMix(
                IPayerMixMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                groupKey is null ? null : "Fixed wing",
                [new PayerMixShare("commercial", "Commercial", 3, 2), new PayerMixShare("va", "VA", 5, 4)]));
        }
    }
}
