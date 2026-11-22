using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Insights;
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
    private readonly FakePaymentAge paymentAge = new();
    private readonly FakeCashPerTransport cashPerTransport = new();
    private readonly FakeNetCashByGroup byGroup = new();
    private readonly FakeInsights insights = new();
    private readonly FakeTransportVolume transports = new();
    private readonly FakeCleanClaims cleanClaims = new();
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
            services.RemoveAll<IInsightService>();
            services.AddSingleton<IInsightService>(insights);
            services.RemoveAll<INetCashByGroupMetric>();
            services.AddSingleton<INetCashByGroupMetric>(byGroup);
            services.RemoveAll<ICashPerTransportMetric>();
            services.AddSingleton<ICashPerTransportMetric>(cashPerTransport);
            services.RemoveAll<IPaymentAgeMetric>();
            services.AddSingleton<IPaymentAgeMetric>(paymentAge);
            services.RemoveAll<ITransportVolumeMetric>();
            services.AddSingleton<ITransportVolumeMetric>(transports);
            services.RemoveAll<ICleanClaimRateMetric>();
            services.AddSingleton<ICleanClaimRateMetric>(cleanClaims);
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
        Assert.Equal((100, new ApPeriod(2026, 10), 3, ServiceLineFilter.Both), metric.LastSeriesCall);
    }

    [Fact]
    public async Task Without_parameters_it_uses_six_months_ending_at_the_newest_period()
    {
        await GetAsync(Series);

        Assert.Equal((100, new ApPeriod(2026, 11), 6, ServiceLineFilter.Both), metric.LastSeriesCall);
    }

    [Theory]
    [InlineData("air", ServiceLineFilter.Air)]
    [InlineData("ground", ServiceLineFilter.Ground)]
    [InlineData("all", ServiceLineFilter.Both)]
    public async Task The_series_can_be_asked_for_one_service_line(string value, ServiceLineFilter expected)
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}?endPeriod=202610&serviceLine={value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected, metric.LastSeriesCall.ServiceLine);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("1")]
    [InlineData("air,ground")]
    [InlineData("AIR")]
    public async Task A_series_service_line_that_is_not_all_air_or_ground_is_a_400_and_asks_nothing(string value)
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}?serviceLine={value}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, metric.SeriesCalls);
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

    [Fact]
    public async Task Payment_age_returns_cells_by_payer_posting_month_and_age_for_the_chosen_group()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payment-age?period=202610&serviceLine=air&group=fixed-wing");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("payment_age", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("202610", "air", "fixed-wing"), (body.GetProperty("period").GetString(), body.GetProperty("serviceLine").GetString(), body.GetProperty("group").GetString()));
        JsonElement cell = body.GetProperty("cells")[0];
        Assert.Equal(("commercial", "Commercial", "202610", 6, 3300m, 2), (cell.GetProperty("payerKey").GetString(), cell.GetProperty("payerName").GetString(), cell.GetProperty("postingPeriod").GetString(), cell.GetProperty("ageMonths").GetInt32(), cell.GetProperty("cash").GetDecimal(), cell.GetProperty("transports").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing"), paymentAge.LastCall);
    }

    [Fact]
    public async Task Payment_age_without_parameters_uses_the_newest_period_and_air()
    {
        await GetAsync($"{Series}/payment-age");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air, (string?)null), paymentAge.LastCall);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/payment-age")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/payment-age")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/payment-age")]
    public async Task Payment_age_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, paymentAge.Calls);
    }

    [Fact]
    public async Task Payment_age_needs_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/payment-age", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, paymentAge.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    [InlineData("group=Fixed%20Wing")]
    [InlineData("group=fixed-wing%27%3B%20DROP%20TABLE%20x")]
    [InlineData("group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Payment_age_with_bad_parameters_is_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payment-age?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, paymentAge.Calls);
    }

    [Fact]
    public async Task Payment_age_in_a_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        paymentAge.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/payment-age?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cash_per_transport_returns_every_payer_with_the_cohorts_and_names_the_group()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/cash-per-transport?period=202610&serviceLine=air&group=fixed-wing");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cash_per_transport_by_payer", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("202610", "202505", "202604", "air", "fixed-wing"), (body.GetProperty("period").GetString(), body.GetProperty("cohortFrom").GetString(), body.GetProperty("cohortThrough").GetString(), body.GetProperty("serviceLine").GetString(), body.GetProperty("group").GetString()));
        JsonElement commercial = body.GetProperty("payers")[0];
        Assert.Equal(("commercial", 3, 2000m, 666.67m), (commercial.GetProperty("key").GetString(), commercial.GetProperty("transports").GetInt32(), commercial.GetProperty("cash").GetDecimal(), Math.Round(commercial.GetProperty("cashPerTransport").GetDecimal(), 2)));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("payers")[1].GetProperty("cashPerTransport").ValueKind);
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing"), cashPerTransport.LastCall);
    }

    [Fact]
    public async Task Cash_per_transport_without_parameters_uses_the_newest_period_and_air()
    {
        await GetAsync($"{Series}/cash-per-transport");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air, (string?)null), cashPerTransport.LastCall);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/cash-per-transport")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/cash-per-transport")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/cash-per-transport")]
    public async Task Cash_per_transport_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, cashPerTransport.Calls);
    }

    [Fact]
    public async Task Cash_per_transport_needs_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/cash-per-transport", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, cashPerTransport.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    [InlineData("group=Fixed%20Wing")]
    [InlineData("group=fixed-wing%27%3B%20DROP%20TABLE%20x")]
    [InlineData("group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Cash_per_transport_with_bad_parameters_is_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/cash-per-transport?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, cashPerTransport.Calls);
    }

    [Fact]
    public async Task Cash_per_transport_in_a_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        cashPerTransport.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/cash-per-transport?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Net_cash_by_group_returns_points_by_group_and_month()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/by-group?period=202610&serviceLine=air");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("net_cash_by_group", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal(("202610", "air"), (body.GetProperty("period").GetString(), body.GetProperty("serviceLine").GetString()));
        JsonElement point = body.GetProperty("points")[0];
        Assert.Equal(("fixed-wing", "Fixed wing", "air", "202610", 3000m, 4), (point.GetProperty("groupKey").GetString(), point.GetProperty("groupName").GetString(), point.GetProperty("serviceLine").GetString(), point.GetProperty("period").GetString(), point.GetProperty("cash").GetDecimal(), point.GetProperty("transports").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air), byGroup.LastCall);
    }

    [Fact]
    public async Task Net_cash_by_group_without_parameters_uses_the_newest_period_and_air()
    {
        await GetAsync($"{Series}/by-group");

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air), byGroup.LastCall);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/by-group")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/by-group")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/by-group")]
    public async Task Net_cash_by_group_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, byGroup.Calls);
    }

    [Fact]
    public async Task Net_cash_by_group_needs_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/by-group", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, byGroup.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    public async Task Net_cash_by_group_with_bad_parameters_is_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/by-group?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, byGroup.Calls);
    }

    [Fact]
    public async Task Net_cash_by_group_in_a_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        byGroup.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/by-group?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    private const string Insights = "/api/v1/providers/100/insights/what-changed";

    [Fact]
    public async Task What_changed_returns_the_headline_drivers_expected_effects_and_remainder()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Insights}?period=202610&serviceLine=air&group=fixed-wing");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("202610", "202609", "air", "fixed-wing", "Fixed wing", "2026-11-05"), (body.GetProperty("period").GetString(), body.GetProperty("priorPeriod").GetString(), body.GetProperty("serviceLine").GetString(), body.GetProperty("group").GetString(), body.GetProperty("groupName").GetString(), body.GetProperty("dataThrough").GetString()));
        Assert.Equal("Fixed wing cash fell $137,770.00 (22%) from September.", body.GetProperty("headline").GetString());
        Assert.Equal((-137_770m, -0.22m), (body.GetProperty("delta").GetDecimal(), body.GetProperty("deltaRatio").GetDecimal()));
        JsonElement driver = body.GetProperty("drivers")[0];
        Assert.Equal(("volume", "watch", -20_000m), (driver.GetProperty("kind").GetString(), driver.GetProperty("label").GetString(), driver.GetProperty("impact").GetDecimal()));
        Assert.Equal("payer-mix", body.GetProperty("expected")[0].GetProperty("kind").GetString());
        Assert.Equal(("withinNormalRange", -117_770m), (body.GetProperty("remainder").GetProperty("judgment").GetString(), body.GetProperty("remainder").GetProperty("amount").GetDecimal()));
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing"), insights.LastCall);
    }

    [Fact]
    public async Task What_changed_without_parameters_uses_the_newest_period_and_air()
    {
        await GetAsync(Insights);

        Assert.Equal((100, new ApPeriod(2026, 11), ServiceLineFilter.Air, (string?)null), insights.LastCall);
    }

    [Fact]
    public async Task What_changed_with_nothing_to_explain_is_a_204()
    {
        insights.Empty = true;

        (HttpStatusCode status, _) = await GetAsync($"{Insights}?period=202610");

        Assert.Equal(HttpStatusCode.NoContent, status);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/insights/what-changed")]
    [InlineData("/api/v1/providers/200/insights/what-changed")]
    [InlineData("/api/v1/providers/abc/insights/what-changed")]
    public async Task What_changed_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_is_not_found_and_never_computed(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, insights.Calls);
    }

    [Fact]
    public async Task What_changed_needs_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync(Insights, Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, insights.Calls);
    }

    [Fact]
    public async Task What_changed_asks_for_no_authentication_to_be_skipped()
    {
        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"{Insights}?period=202610", TestContext.Current.CancellationToken);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(0, insights.Calls);
    }

    [Theory]
    [InlineData("period=2026-10")]
    [InlineData("period=202613")]
    [InlineData("serviceLine=1")]
    [InlineData("serviceLine=Air,Ground")]
    [InlineData("group=Fixed%20Wing")]
    [InlineData("group=fixed-wing%27%3B%20DROP%20TABLE%20x")]
    [InlineData("group=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task What_changed_with_bad_parameters_is_a_400_problem_and_nothing_is_computed(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Insights}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, insights.Calls);
    }

    [Fact]
    public async Task What_changed_ignores_a_provider_named_in_the_query_or_a_header()
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, $"{Insights}?providerId=999", headers: new Dictionary<string, string> { ["X-Provider-Id"] = "999" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, insights.LastCall.ProviderId);
    }

    [Fact]
    public async Task What_changed_holds_figures_and_words_only_no_payer_or_patient_names()
    {
        (_, JsonElement body) = await GetAsync($"{Insights}?period=202610");

        string text = body.GetRawText();
        Assert.DoesNotContain("AETNA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("primPay", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task What_changed_in_a_warehouse_outage_is_a_503_that_says_nothing_about_why()
    {
        insights.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Insights}?period=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transports_come_back_by_month_with_labels_the_split_of_the_last_month_and_the_open_flag()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/transports?endPeriod=202610&months=3&serviceLine=air");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("transport_volume", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.True(body.GetProperty("periodOpen").GetBoolean());
        JsonElement last = body.GetProperty("points").EnumerateArray().Last();
        Assert.Equal(("202610", "Oct", 2026, 12, 345.5m), (last.GetProperty("period").GetString(), last.GetProperty("label").GetString(), last.GetProperty("year").GetInt32(), last.GetProperty("transports").GetInt32(), last.GetProperty("loadedMiles").GetDecimal()));
        Assert.Equal(("rotor-wing", "air", 8), (body.GetProperty("groups")[0].GetProperty("key").GetString(), body.GetProperty("groups")[0].GetProperty("serviceLine").GetString(), body.GetProperty("groups")[0].GetProperty("transports").GetInt32()));
        Assert.Equal((100, new ApPeriod(2026, 10), 3, ServiceLineFilter.Air), transports.LastCall);
    }

    [Fact]
    public async Task Transports_without_parameters_use_six_months_ending_at_the_newest_period_for_all_service_lines()
    {
        await GetAsync($"{Series}/transports");

        Assert.Equal((100, new ApPeriod(2026, 11), 6, ServiceLineFilter.Both), transports.LastCall);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/transports")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/transports")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/transports")]
    public async Task Transports_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_are_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, transports.Calls);
    }

    [Fact]
    public async Task Transports_need_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/transports", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, transports.Calls);
    }

    [Theory]
    [InlineData("months=0")]
    [InlineData("months=25")]
    [InlineData("months=-3")]
    [InlineData("endPeriod=2026-10")]
    [InlineData("endPeriod=202613")]
    [InlineData("serviceLine=2")]
    [InlineData("serviceLine=air,ground")]
    public async Task Transports_with_bad_parameters_are_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/transports?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, transports.Calls);
    }

    [Fact]
    public async Task Transports_in_a_warehouse_outage_are_a_503_that_says_nothing_about_why()
    {
        transports.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/transports?endPeriod=202610");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret.internal", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_claims_come_back_by_month_with_the_rate_the_load_date_and_no_patient_detail()
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/clean-claims?endPeriod=202603&months=2");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("clean_claim_rate", body.GetProperty("metric").GetProperty("key").GetString());
        Assert.Equal("2026-05-07", body.GetProperty("snapshotDate").GetString());
        JsonElement last = body.GetProperty("points").EnumerateArray().Last();
        Assert.Equal(("202603", "Mar", 2026, 1849, 59, 1908), (last.GetProperty("period").GetString(), last.GetProperty("label").GetString(), last.GetProperty("year").GetInt32(), last.GetProperty("cleanClaims").GetInt32(), last.GetProperty("rejectedClaims").GetInt32(), last.GetProperty("totalClaims").GetInt32()));
        Assert.Equal(0.9691m, last.GetProperty("rate").GetDecimal());
        Assert.Equal((100, new ApPeriod(2026, 3), 2), cleanClaims.LastCall);
    }

    [Fact]
    public async Task Clean_claims_without_parameters_use_six_months_ending_at_the_newest_period()
    {
        await GetAsync($"{Series}/clean-claims");

        Assert.Equal((100, new ApPeriod(2026, 11), 6), cleanClaims.LastCall);
    }

    [Fact]
    public async Task A_month_with_no_claims_has_a_null_rate_not_zero()
    {
        (_, JsonElement body) = await GetAsync($"{Series}/clean-claims?endPeriod=202604&months=1");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("points")[0].GetProperty("rate").ValueKind);
    }

    [Theory]
    [InlineData("/api/v1/providers/999/metrics/net-collections/clean-claims")]
    [InlineData("/api/v1/providers/200/metrics/net-collections/clean-claims")]
    [InlineData("/api/v1/providers/abc/metrics/net-collections/clean-claims")]
    public async Task Clean_claims_for_a_provider_the_user_is_not_assigned_to_or_is_limited_inside_are_not_found_and_never_queried(string url)
    {
        (HttpStatusCode status, _) = await GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, cleanClaims.Calls);
    }

    [Fact]
    public async Task Clean_claims_need_the_dashboard_permission()
    {
        (HttpStatusCode status, _) = await GetAsync($"{Series}/clean-claims", Permissions.App);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, cleanClaims.Calls);
    }

    [Theory]
    [InlineData("months=0")]
    [InlineData("months=25")]
    [InlineData("endPeriod=2026-03")]
    [InlineData("endPeriod=202613")]
    public async Task Clean_claims_with_bad_parameters_are_a_400_problem_and_nothing_is_queried(string query)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/clean-claims?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.Equal(0, cleanClaims.Calls);
    }

    [Fact]
    public async Task Clean_claims_in_a_warehouse_outage_are_a_503_that_says_nothing_about_why()
    {
        cleanClaims.Failure = new WarehouseUnavailableException(new InvalidOperationException("Host=secret.internal Password=hunter2"));

        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/clean-claims?endPeriod=202603");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class FakeNetCollections : INetCollectionsMetric
    {
        public List<ApPeriod> Periods { get; } = [new(2026, 11), new(2026, 10)];

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public int SeriesCalls { get; private set; }

        public (int ProviderId, ApPeriod End, int Months, ServiceLineFilter ServiceLine) LastSeriesCall { get; private set; }

        public Task<IReadOnlyList<ApPeriod>> GetPeriodsAsync(int providerId, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null ? Task.FromResult<IReadOnlyList<ApPeriod>>([.. Periods]) : Task.FromException<IReadOnlyList<ApPeriod>>(Failure);
        }

        public Task<NetCollectionsSeries> GetSeriesAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Calls++;
            SeriesCalls++;
            LastSeriesCall = (providerId, endPeriod, months, serviceLine);
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

    private sealed class FakeInsights : IInsightService
    {
        public Exception? Failure { get; set; }

        public bool Empty { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group) LastCall { get; private set; }

        public Task<InsightResult?> GetWhatChangedAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine, groupKey);
            if (Failure is not null)
            {
                return Task.FromException<InsightResult?>(Failure);
            }

            if (Empty)
            {
                return Task.FromResult<InsightResult?>(null);
            }

            var volume = new VolumeFinding(FindingLabel.Watch, -20_000m, 100m, 80m);
            var mix = new PayerMixFinding(FindingLabel.Watch, -38_400m, "medicaid", "Medicaid", 0.18m, 0.23m);
            var insight = new Insight(
                new InsightHeadline(period, period.AddMonths(-1), groupKey, "Fixed wing", 487_230m, 625_000m, -137_770m, -0.22m),
                [volume],
                [mix],
                new Remainder(-117_770m, RemainderJudgment.WithinNormalRange),
                null);
            return Task.FromResult<InsightResult?>(new InsightResult(insight, InsightText.Build(insight), new DateOnly(2026, 11, 5)));
        }
    }

    private sealed class FakeNetCashByGroup : INetCashByGroupMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine) LastCall { get; private set; }

        public Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine);
            if (Failure is not null)
            {
                return Task.FromException<NetCashByGroup>(Failure);
            }

            return Task.FromResult(new NetCashByGroup(
                INetCashByGroupMetric.Definition,
                period,
                INetCashByGroupMetric.MonthsBefore,
                serviceLine,
                [new NetCashByGroupPoint("fixed-wing", "Fixed wing", true, period, 3000m, 4)]));
        }
    }

    private sealed class FakeCashPerTransport : ICashPerTransportMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group) LastCall { get; private set; }

        public Task<CashPerTransport> GetCashPerTransportAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine, groupKey);
            if (Failure is not null)
            {
                return Task.FromException<CashPerTransport>(Failure);
            }

            return Task.FromResult(new CashPerTransport(
                ICashPerTransportMetric.Definition,
                period,
                period.AddMonths(-17),
                period.AddMonths(-6),
                serviceLine,
                groupKey,
                [new PayerCashPerTransport("commercial", "Commercial", 3, 2000m), new PayerCashPerTransport("va", "VA", 0, 0m)]));
        }
    }

    private sealed class FakePaymentAge : IPaymentAgeMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter ServiceLine, string? Group) LastCall { get; private set; }

        public Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, period, serviceLine, groupKey);
            if (Failure is not null)
            {
                return Task.FromException<PaymentAge>(Failure);
            }

            return Task.FromResult(new PaymentAge(
                IPaymentAgeMetric.Definition,
                period,
                IPaymentAgeMetric.MonthsBefore,
                serviceLine,
                groupKey,
                [new PaymentAgeCell("commercial", "Commercial", period, 6, 3300m, 2)]));
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

    private sealed class FakeTransportVolume : ITransportVolumeMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod End, int Months, ServiceLineFilter ServiceLine) LastCall { get; private set; }

        public Task<TransportVolume> GetVolumeAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, endPeriod, months, serviceLine);
            if (Failure is not null)
            {
                return Task.FromException<TransportVolume>(Failure);
            }

            List<TransportVolumePoint> points = Enumerable.Range(0, months).Select(i => new TransportVolumePoint(endPeriod.AddMonths(-(months - 1 - i)), 10 + i, 300m + (i * 15.5m))).ToList();
            return Task.FromResult(new TransportVolume(
                ITransportVolumeMetric.Definition,
                endPeriod,
                months,
                serviceLine,
                endPeriod.Month == 10,
                points.Take(months - 1).Append(new TransportVolumePoint(endPeriod, 12, 345.5m)).ToList(),
                [new TransportVolumeGroup("rotor-wing", "Rotor wing", true, 8), new TransportVolumeGroup("fixed-wing", "Fixed wing", true, 4)]));
        }
    }

    private sealed class FakeCleanClaims : ICleanClaimRateMetric
    {
        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public (int ProviderId, ApPeriod End, int Months) LastCall { get; private set; }

        public Task<CleanClaimRate> GetRateAsync(int providerId, ApPeriod endPeriod, int months, CancellationToken cancellationToken)
        {
            Calls++;
            LastCall = (providerId, endPeriod, months);
            if (Failure is not null)
            {
                return Task.FromException<CleanClaimRate>(Failure);
            }

            List<CleanClaimPoint> points = [new(new ApPeriod(2026, 2), 2767, 1, 2768), new(endPeriod, endPeriod.Month == 4 ? 0 : 1849, endPeriod.Month == 4 ? 0 : 59, endPeriod.Month == 4 ? 0 : 1908)];
            return Task.FromResult(new CleanClaimRate(ICleanClaimRateMetric.Definition, endPeriod, months, new DateOnly(2026, 5, 7), points.TakeLast(months).ToList()));
        }
    }
}
