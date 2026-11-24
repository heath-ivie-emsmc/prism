using System.Net;
using System.Text.Json;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

public sealed partial class FleetMetricEndpointTests
{
    private sealed partial class FakeMetrics : ICashContributionByAircraftMetric, ICashContributionByDivisionMetric, ICashContributionByPayerMetric
    {
        public (string Metric, int ProviderId, ApPeriod Period, ServiceLineFilter Line, string? Group, string? Division) LastContribution { get; private set; }

        private Task<CashContribution> Contribution(MetricDefinition definition, int providerId, ApPeriod period, ServiceLineFilter line, string? group, ReportingDivision? division)
        {
            Count();
            LastContribution = (definition.Key, providerId, period, line, group, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<CashContribution>(Failure);
            }

            // 500 up, 300 down and an Other row of 40 up: the change is 240, and the rows add up to it.
            return Task.FromResult(CashContribution.Of(
                definition,
                period,
                line,
                group,
                [
                    new ContributionRow("commercial", Text, false, 1500m, 1000m),
                    new ContributionRow("medicare", "Medicare", false, 200m, 500m),
                    new ContributionRow(null, "Other", true, 140m, 100m),
                ]));
        }

        Task<CashContribution> ICashContributionByAircraftMetric.GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Contribution(ICashContributionByAircraftMetric.Definition, providerId, period, serviceLine, groupKey, division);

        Task<CashContribution> ICashContributionByDivisionMetric.GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Contribution(ICashContributionByDivisionMetric.Definition, providerId, period, serviceLine, groupKey, division);

        Task<CashContribution> ICashContributionByPayerMetric.GetContributionAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Contribution(ICashContributionByPayerMetric.Definition, providerId, period, serviceLine, groupKey, division);
    }

    private static readonly string[] ContributionRoutes = ["cash-contribution/aircraft", "cash-contribution/division", "cash-contribution/payer"];

    public static TheoryData<string> ContributionRouteData()
    {
        var data = new TheoryData<string>();
        foreach (string route in ContributionRoutes)
        {
            data.Add(route);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContributionRouteData))]
    public async Task A_contribution_route_returns_rows_that_add_up_exactly_to_the_total_change_and_the_scope_it_was_asked_for(string route)
    {
        (HttpStatusCode status, JsonElement body) = await GetAsync($"{Series}/{route}?period=202610&serviceLine=air&group=rotor-wing&division=rotor-base");

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement total = body.GetProperty("total");
        JsonElement[] rows = [.. body.GetProperty("rows").EnumerateArray()];
        Assert.Equal((1840m, 1600m, 240m, 15.0m), (total.GetProperty("netCash").GetDecimal(), total.GetProperty("priorNetCash").GetDecimal(), total.GetProperty("change").GetDecimal(), total.GetProperty("changePercent").GetDecimal()));
        Assert.Equal(total.GetProperty("change").GetDecimal(), rows.Sum(r => r.GetProperty("change").GetDecimal()));
        Assert.Equal(total.GetProperty("netCash").GetDecimal(), rows.Sum(r => r.GetProperty("netCash").GetDecimal()));
        Assert.Equal(total.GetProperty("priorNetCash").GetDecimal(), rows.Sum(r => r.GetProperty("priorNetCash").GetDecimal()));
        Assert.Equal([500m, -300m, 40m], rows.Select(r => r.GetProperty("change").GetDecimal()));
        Assert.Equal([2.0833m, -1.25m, 0.1667m], rows.Select(r => r.GetProperty("shareOfChange").GetDecimal()));
        Assert.Equal([false, false, true], rows.Select(r => r.GetProperty("isOther").GetBoolean()));
        Assert.Equal(("rotor-wing", "Mercy Flights - Rotor", ServiceLineFilter.Air), (body.GetProperty("group").GetString(), fakes.LastContribution.Division, fakes.LastContribution.Line));
        Assert.Equal(("rotor-wing", 100), (fakes.LastContribution.Group, fakes.LastContribution.ProviderId));
    }

    [Fact]
    public async Task Only_the_payer_contribution_issues_a_drill_and_only_for_a_payer_group_that_has_detail()
    {
        (_, JsonElement payer) = await GetAsync($"{Series}/cash-contribution/payer?period=202610");
        (_, JsonElement division) = await GetAsync($"{Series}/cash-contribution/division?period=202610");

        JsonElement[] rows = [.. payer.GetProperty("rows").EnumerateArray()];
        string href = rows[0].GetProperty("links")[0].GetProperty("href").GetString()!;
        Assert.StartsWith("/api/v1/providers/100/metrics/net-collections/payer-mix/commercial?", href, StringComparison.Ordinal);
        Assert.Contains("period=202610", href, StringComparison.Ordinal);
        Assert.Equal(1, rows[1].GetProperty("links").GetArrayLength());
        Assert.Equal(0, rows[2].GetProperty("links").GetArrayLength());
        Assert.All(division.GetProperty("rows").EnumerateArray(), r => Assert.Equal(0, r.GetProperty("links").GetArrayLength()));
    }

    [Theory]
    [MemberData(nameof(ContributionRouteData))]
    public async Task A_contribution_response_has_no_patient_claim_or_address_fields(string route)
    {
        (_, JsonElement body) = await GetAsync($"{Series}/{route}?period=202610");

        string[] names = [.. PropertyNames(body).Select(n => n.ToLowerInvariant())];
        string[] forbidden = ["patient", "claim", "address", "city", "zip", "encounter", "narrative", "dob", "gps"];
        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(ContributionRouteData))]
    public async Task A_contribution_answer_has_a_strong_etag_and_a_repeat_with_it_is_a_304_and_another_period_is_not(string route)
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage first = await client.GetAsync($"{Series}/{route}?period=202610");
        using HttpResponseMessage second = await client.SendAsync(HttpMethod.Get, $"{Series}/{route}?period=202610", headers: new Dictionary<string, string> { ["If-None-Match"] = first.Headers.ETag!.Tag });
        using HttpResponseMessage other = await client.SendAsync(HttpMethod.Get, $"{Series}/{route}?period=202609", headers: new Dictionary<string, string> { ["If-None-Match"] = first.Headers.ETag.Tag });

        Assert.False(first.Headers.ETag.IsWeak);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ContributionRouteData))]
    public async Task A_caller_who_may_not_read_the_provider_gets_a_404_with_no_etag_even_with_a_wildcard_if_none_match(string route)
    {
        var headers = new Dictionary<string, string> { ["If-None-Match"] = "*" };
        foreach (string provider in new[] { "101", "200", "999" })
        {
            using CookieClient client = factory.NewSignedInClient();
            using HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, $"/api/v1/providers/{provider}/metrics/net-collections/{route}?period=202610", headers: headers);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.ETag);
        }

        Assert.Equal(0, fakes.TotalCalls);
    }

    [Fact]
    public async Task The_aircraft_contribution_is_air_only_all_means_air_and_ground_is_a_400_that_reads_nothing()
    {
        (HttpStatusCode all, JsonElement body) = await GetAsync($"{Series}/cash-contribution/aircraft?period=202610&serviceLine=all");
        int calls = fakes.TotalCalls;
        (HttpStatusCode ground, string text) = await GetRawAsync($"{Series}/cash-contribution/aircraft?period=202610&serviceLine=ground");
        (HttpStatusCode division, _) = await GetAsync($"{Series}/cash-contribution/division?period=202610&serviceLine=ground");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.OK), (all, ground, division));
        Assert.Equal("air", body.GetProperty("serviceLine").GetString());
        Assert.Contains("air only", text, StringComparison.Ordinal);
        Assert.Equal(calls, fakes.TotalCalls - 1);
        Assert.Equal(ServiceLineFilter.Ground, fakes.LastContribution.Line);
    }

    [Fact]
    public async Task A_facility_restricted_user_gets_a_404_and_a_Provider_in_the_route_is_the_one_the_metric_reads()
    {
        (HttpStatusCode limited, _) = await GetRawAsync($"/api/v1/providers/200/metrics/net-collections/cash-contribution/payer?period=202610&division=rotor-base");
        (HttpStatusCode own, _) = await GetAsync($"{Series}/cash-contribution/payer?period=202610");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.OK), (limited, own));
        Assert.Equal(100, fakes.LastContribution.ProviderId);
        Assert.Equal(1, fakes.TotalCalls);
    }
}
