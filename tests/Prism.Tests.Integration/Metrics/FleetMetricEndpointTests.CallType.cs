using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Tests.Integration.Metrics;

public sealed partial class FleetMetricEndpointTests
{
    /// <summary>The fakes of every Metric these routes serve. Each counts its calls, and a failure or a hostile text set here shows in every answer.</summary>
    private sealed partial class FakeMetrics : ITransportMixByCallTypeMetric, IAircraftPerformanceMetric, IBaseAnalysisMetric, ILoadedMilesByAircraftMetric, IPayerMixFinancialEffectMetric, ITransportsByPickupLocationMetric
    {
        public Exception? Failure { get; set; }

        public string Text { get; set; } = "TEXT";

        public int TotalCalls { get; private set; }

        public (int ProviderId, ApPeriod Period, int Months, ServiceLineFilter Line, string? Group, string? Division) LastMix { get; private set; }

        private void Count() => TotalCalls++;

        public (int ProviderId, ApPeriod Period, ServiceLineFilter Line, string? Group, string? Division) LastPerformance { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter Line, string? Group, string? Division) LastAnalysis { get; private set; }

        public (int ProviderId, ApPeriod Period, int Months, ServiceLineFilter Line, string? Group, string? Division) LastMiles { get; private set; }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter Line, string? Group, string? Division) LastEffect { get; private set; }

        public bool Comparable { get; set; } = true;

        public Task<PayerMixFinancialEffect> GetEffectAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastEffect = (providerId, period, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<PayerMixFinancialEffect>(Failure);
            }

            return Task.FromResult(new PayerMixFinancialEffect(
                IPayerMixFinancialEffectMetric.Definition,
                period,
                period.AddMonths(-1),
                period.AddMonths(-17),
                period.AddMonths(-6),
                serviceLine,
                groupKey,
                100,
                Comparable ? 100 : 0,
                [
                    new PayerMixEffectRow(new PayerMixShare("commercial", Text, 77, 82), 0.82m, 0.77m, 1000.456m, Comparable ? -5000.126m : null),
                    new PayerMixEffectRow(new PayerMixShare("va", "VA", 0, 0), 0m, 0m, null, null),
                ],
                Comparable ? -5000.126m : null));
        }

        public (int ProviderId, ApPeriod Period, ServiceLineFilter Line, string? Group, string? Division) LastPickups { get; private set; }

        public Task<TransportsByPickupLocation> GetPickupsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastPickups = (providerId, period, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<TransportsByPickupLocation>(Failure);
            }

            return Task.FromResult(new TransportsByPickupLocation(
                ITransportsByPickupLocationMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                [
                    new PickupSource("hospital", "Hospital", true, 6, 4, 600m, 400m, 1000m, 300m, [new PayerCount("commercial", "Commercial", 3, 2)]),
                    new PickupSource("scene", "Scene", false, 4, 6, 400m, 600m, 0m, 0m, []),
                ],
                [new PickupFacility("hospital", Text, false, 5, 3, 500m), new PickupFacility(null, "Other", true, 1, 1, 100m)]));
        }

        public Task<BaseAnalysis> GetAnalysisAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastAnalysis = (providerId, period, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<BaseAnalysis>(Failure);
            }

            var main = new PeriodFigures(3, 2, 300m, 200m, 900m, 400m);
            return Task.FromResult(new BaseAnalysis(
                IBaseAnalysisMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                main,
                [
                    new DivisionRow("rotor-base", Text, false, main, [new DivisionTail("N123AB", false, 2), new DivisionTail("Other", true, 1)]),
                    new DivisionRow(null, "Other", true, PeriodFigures.None, []),
                ]));
        }

        public Task<LoadedMilesByAircraft> GetMilesAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastMiles = (providerId, period, months, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<LoadedMilesByAircraft>(Failure);
            }

            return Task.FromResult(new LoadedMilesByAircraft(
                ILoadedMilesByAircraftMetric.Definition,
                period,
                months,
                serviceLine,
                groupKey,
                [
                    new AircraftMilesRow(Text, false, 2, 150m, [.. Enumerable.Range(0, months).Select(i => new AircraftMilesPoint(period.AddMonths(-(months - 1 - i)), i == months - 1 ? 2 : 0, i == months - 1 ? 150m : 0m))]),
                    new AircraftMilesRow("Other", true, 0, 0m, [.. Enumerable.Range(0, months).Select(i => new AircraftMilesPoint(period.AddMonths(-(months - 1 - i)), 0, 0m))]),
                ]));
        }

        public Task<AircraftPerformance> GetPerformanceAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastPerformance = (providerId, period, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<AircraftPerformance>(Failure);
            }

            var main = new PeriodFigures(3, 2, 300m, 200m, 900m, 400m);
            var other = new PeriodFigures(1, 0, 10m, 0m, 0m, 0m);
            return Task.FromResult(new AircraftPerformance(
                IAircraftPerformanceMetric.Definition,
                period,
                period.AddMonths(-1),
                serviceLine,
                groupKey,
                main.Plus(other),
                [new AircraftRow(Text, false, main), new AircraftRow("Other", true, other)]));
        }

        public Task<TransportMixByCallType> GetMixAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Count();
            LastMix = (providerId, period, months, serviceLine, groupKey, division?.Name);
            if (Failure is not null)
            {
                return Task.FromException<TransportMixByCallType>(Failure);
            }

            ApPeriod prior = period.AddMonths(-1);
            return Task.FromResult(new TransportMixByCallType(
                ITransportMixByCallTypeMetric.Definition,
                period,
                prior,
                months,
                serviceLine,
                groupKey,
                [
                    new CallTypeRow("scene", Text, 6, 4, 600m, 400m, 1000m, 300m, [new PayerCount("commercial", "Commercial", 3, 2), new PayerCount("medicare", "Medicare", 3, 0)]),
                    new CallTypeRow("interfacility", "Interfacility", 4, 6, 400m, 600m, 480m, 0m, [new PayerCount("commercial", "Commercial", 4, 6)]),
                ],
                [.. Enumerable.Range(0, months).Select(i => new CallTypeTrendPoint(period.AddMonths(-(months - 1 - i)), [new CallTypeCount("scene", i), new CallTypeCount("interfacility", 0)]))]));
        }
    }

    [Fact]
    public async Task The_call_type_route_returns_counts_shares_changes_payer_mix_net_cash_and_the_trend()
    {
        (_, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/transport-mix-by-call-type?period=202610&serviceLine=air&group=fixed-wing&months=12&division=rotor-base");

        Assert.Equal((12, 10, 10, "air", "fixed-wing"), (body.GetProperty("months").GetInt32(), body.GetProperty("totalTransports").GetInt32(), body.GetProperty("priorTotalTransports").GetInt32(), body.GetProperty("serviceLine").GetString(), body.GetProperty("group").GetString()));
        System.Text.Json.JsonElement scene = body.GetProperty("callTypes")[0];
        Assert.Equal(("scene", 6, 4, 2, 0.6m, 0.4m, 20.0m, 600m, 1000m, 300m), (
            scene.GetProperty("key").GetString(), scene.GetProperty("transports").GetInt32(), scene.GetProperty("priorTransports").GetInt32(), scene.GetProperty("changeTransports").GetInt32(),
            scene.GetProperty("share").GetDecimal(), scene.GetProperty("priorShare").GetDecimal(), scene.GetProperty("changePoints").GetDecimal(), scene.GetProperty("loadedMiles").GetDecimal(), scene.GetProperty("netCash").GetDecimal(), scene.GetProperty("priorNetCash").GetDecimal()));
        System.Text.Json.JsonElement medicare = scene.GetProperty("payerMix")[1];
        Assert.Equal(("medicare", 3, 0, 0.5m, 0m), (medicare.GetProperty("key").GetString(), medicare.GetProperty("transports").GetInt32(), medicare.GetProperty("priorTransports").GetInt32(), medicare.GetProperty("share").GetDecimal(), medicare.GetProperty("priorShare").GetDecimal()));
        Assert.Equal(12, body.GetProperty("trend").GetArrayLength());
        System.Text.Json.JsonElement last = body.GetProperty("trend")[11];
        Assert.Equal(("202610", "Oct", 2026, 11), (last.GetProperty("period").GetString(), last.GetProperty("label").GetString(), last.GetProperty("year").GetInt32(), last.GetProperty("transports").GetInt32()));
        Assert.Equal(("scene", "Scene", 11, 1m), (last.GetProperty("callTypes")[0].GetProperty("key").GetString(), last.GetProperty("callTypes")[0].GetProperty("name").GetString(), last.GetProperty("callTypes")[0].GetProperty("transports").GetInt32(), last.GetProperty("callTypes")[0].GetProperty("share").GetDecimal()));
        Assert.Equal((100, new ApPeriod(2026, 10), 12, ServiceLineFilter.Air, "fixed-wing", "Mercy Flights - Rotor"), fakes.LastMix);
    }

    [Fact]
    public async Task The_aircraft_performance_route_returns_figures_changes_and_net_cash_per_transport_for_each_tail()
    {
        (HttpStatusCode status, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/aircraft-performance?period=202610&group=fixed-wing&division=rotor-base");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["aircraft", "dataThrough", "division", "divisionName", "group", "metric", "period", "priorPeriod", "providerId", "serviceLine", "totals"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        System.Text.Json.JsonElement tail = body.GetProperty("aircraft")[0];
        Assert.Equal(("TEXT", false), (tail.GetProperty("tail").GetString(), tail.GetProperty("isOther").GetBoolean()));
        System.Text.Json.JsonElement f = tail.GetProperty("figures");
        Assert.Equal((3, 2, 1, 50.0m, 300m, 200m, 100m, 50.0m, 900m, 400m, 500m, 125.0m, 300.00m, 200.00m), (
            f.GetProperty("transports").GetInt32(), f.GetProperty("priorTransports").GetInt32(), f.GetProperty("changeTransports").GetInt32(), f.GetProperty("changeTransportsPercent").GetDecimal(),
            f.GetProperty("loadedMiles").GetDecimal(), f.GetProperty("priorLoadedMiles").GetDecimal(), f.GetProperty("changeLoadedMiles").GetDecimal(), f.GetProperty("changeLoadedMilesPercent").GetDecimal(),
            f.GetProperty("netCash").GetDecimal(), f.GetProperty("priorNetCash").GetDecimal(), f.GetProperty("changeNetCash").GetDecimal(), f.GetProperty("changeNetCashPercent").GetDecimal(),
            f.GetProperty("netCashPerTransport").GetDecimal(), f.GetProperty("priorNetCashPerTransport").GetDecimal()));
        System.Text.Json.JsonElement other = body.GetProperty("aircraft")[1];
        Assert.True(other.GetProperty("isOther").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, other.GetProperty("figures").GetProperty("changeTransportsPercent").ValueKind);
        Assert.Equal(4, body.GetProperty("totals").GetProperty("transports").GetInt32());
        Assert.Equal((100, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing", "Mercy Flights - Rotor"), fakes.LastPerformance);
    }

    [Fact]
    public async Task The_payer_mix_effect_route_says_it_is_expected_and_the_recurring_effect_is_the_same_number()
    {
        (_, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/payer-mix-financial-effect?period=202610&serviceLine=all");

        Assert.Equal(("expected", 100, 100, "all"), (body.GetProperty("basis").GetString(), body.GetProperty("transports").GetInt32(), body.GetProperty("priorTransports").GetInt32(), body.GetProperty("serviceLine").GetString()));
        Assert.Equal(-5000.13m, body.GetProperty("estimatedEffect").GetDecimal());
        Assert.Equal(body.GetProperty("estimatedEffect").GetDecimal(), body.GetProperty("recurringMonthlyEffect").GetDecimal());
        Assert.Equal(("202505", "202604", "202610"), (body.GetProperty("cohorts").GetProperty("firstServiceMonth").GetString(), body.GetProperty("cohorts").GetProperty("lastServiceMonth").GetString(), body.GetProperty("cohorts").GetProperty("cashThroughPeriod").GetString()));
        System.Text.Json.JsonElement commercial = body.GetProperty("groups")[0];
        Assert.Equal((0.7700m, 0.8200m, -5.0m, 1000.46m, -5000.13m), (commercial.GetProperty("share").GetDecimal(), commercial.GetProperty("priorShare").GetDecimal(), commercial.GetProperty("changePoints").GetDecimal(), commercial.GetProperty("cashPerTransport").GetDecimal(), commercial.GetProperty("effect").GetDecimal()));
        System.Text.Json.JsonElement va = body.GetProperty("groups")[1];
        Assert.Equal(System.Text.Json.JsonValueKind.Null, va.GetProperty("cashPerTransport").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, va.GetProperty("effect").ValueKind);
    }

    [Fact]
    public async Task The_payer_mix_effect_route_returns_null_figures_cleanly_when_it_cannot_be_worked_out()
    {
        fakes.Comparable = false;

        (HttpStatusCode status, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/payer-mix-financial-effect?period=202610");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("estimatedEffect").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("recurringMonthlyEffect").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("groups")[0].GetProperty("effect").ValueKind);
    }

    [Fact]
    public async Task The_pickup_route_returns_sources_with_shares_payer_mix_net_cash_and_facilities_and_no_address_fields()
    {
        (_, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/transports-by-pickup-location?period=202610");

        Assert.Equal((10, 10), (body.GetProperty("totalTransports").GetInt32(), body.GetProperty("priorTotalTransports").GetInt32()));
        System.Text.Json.JsonElement hospital = body.GetProperty("sources")[0];
        Assert.Equal(("hospital", true, 0.6m, 0.4m, 20.0m, 1000m, 300m), (hospital.GetProperty("key").GetString(), hospital.GetProperty("facilityBased").GetBoolean(), hospital.GetProperty("share").GetDecimal(), hospital.GetProperty("priorShare").GetDecimal(), hospital.GetProperty("changePoints").GetDecimal(), hospital.GetProperty("netCash").GetDecimal(), hospital.GetProperty("priorNetCash").GetDecimal()));
        Assert.Equal(("commercial", 0.5m), (hospital.GetProperty("payerMix")[0].GetProperty("key").GetString(), hospital.GetProperty("payerMix")[0].GetProperty("share").GetDecimal()));
        Assert.False(body.GetProperty("sources")[1].GetProperty("facilityBased").GetBoolean());
        Assert.Equal(2, body.GetProperty("facilities").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("facilities")[1].GetProperty("sourceKey").ValueKind);
        // Property names, not the description text: no field in the answer is an address, city, state or zip.
        string[] names = [.. PropertyNames(body)];
        Assert.DoesNotContain(names, n => n.Contains("address", StringComparison.OrdinalIgnoreCase) || n.Contains("zip", StringComparison.OrdinalIgnoreCase) || n.Contains("city", StringComparison.OrdinalIgnoreCase) || n.Contains("state", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_base_analysis_route_returns_figures_and_the_tails_of_each_division()
    {
        (HttpStatusCode status, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/base-analysis?period=202610&serviceLine=ground");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ground", body.GetProperty("serviceLine").GetString());
        System.Text.Json.JsonElement row = body.GetProperty("divisions")[0];
        Assert.Equal(("rotor-base", "TEXT", false), (row.GetProperty("key").GetString(), row.GetProperty("name").GetString(), row.GetProperty("isOther").GetBoolean()));
        Assert.Equal(500m, row.GetProperty("figures").GetProperty("changeNetCash").GetDecimal());
        Assert.Equal(300.00m, row.GetProperty("figures").GetProperty("netCashPerTransport").GetDecimal());
        Assert.Equal(("N123AB", 2, true), (row.GetProperty("aircraft")[0].GetProperty("tail").GetString(), row.GetProperty("aircraft")[0].GetProperty("transports").GetInt32(), row.GetProperty("aircraft")[1].GetProperty("isOther").GetBoolean()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("divisions")[1].GetProperty("key").ValueKind);
        Assert.Equal(ServiceLineFilter.Ground, fakes.LastAnalysis.Line);
    }

    [Fact]
    public async Task The_loaded_miles_route_returns_the_average_and_a_trend_per_tail()
    {
        (HttpStatusCode status, System.Text.Json.JsonElement body) = await GetAsync($"{Series}/loaded-miles-by-aircraft?period=202610&months=12");

        Assert.Equal(HttpStatusCode.OK, status);
        System.Text.Json.JsonElement tail = body.GetProperty("aircraft")[0];
        Assert.Equal((2, 150m, 75.0m, 12), (tail.GetProperty("transports").GetInt32(), tail.GetProperty("loadedMiles").GetDecimal(), tail.GetProperty("averageLoadedMiles").GetDecimal(), tail.GetProperty("trend").GetArrayLength()));
        System.Text.Json.JsonElement first = tail.GetProperty("trend")[0];
        Assert.Equal(("202511", "Nov", 2025, 0), (first.GetProperty("period").GetString(), first.GetProperty("label").GetString(), first.GetProperty("year").GetInt32(), first.GetProperty("transports").GetInt32()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("averageLoadedMiles").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("aircraft")[1].GetProperty("averageLoadedMiles").ValueKind);
        Assert.Equal(12, fakes.LastMiles.Months);
    }

    [Theory]
    [InlineData("aircraft-performance")]
    [InlineData("loaded-miles-by-aircraft")]
    public async Task An_air_only_metric_treats_all_as_air_and_refuses_ground_with_a_400_that_does_not_echo_the_value(string route)
    {
        (HttpStatusCode all, System.Text.Json.JsonElement allBody) = await GetAsync($"{Series}/{route}?period=202610&serviceLine=all");
        (HttpStatusCode air, _) = await GetAsync($"{Series}/{route}?period=202610&serviceLine=air");
        int callsBefore = fakes.TotalCalls;
        (HttpStatusCode ground, string text) = await GetRawAsync($"{Series}/{route}?period=202610&serviceLine=ground");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.BadRequest), (all, air, ground));
        Assert.Equal("air", allBody.GetProperty("serviceLine").GetString());
        Assert.Equal(ServiceLineFilter.Air, route == "aircraft-performance" ? fakes.LastPerformance.Line : fakes.LastMiles.Line);
        Assert.Contains("air only", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ground", text.Replace("air only", string.Empty, StringComparison.Ordinal).Replace("serviceLine", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(callsBefore, fakes.TotalCalls);
    }

    private static IEnumerable<string> PropertyNames(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (string inner in PropertyNames(property.Value))
                    {
                        yield return inner;
                    }
                }

                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
                {
                    foreach (string inner in PropertyNames(item))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }
}
