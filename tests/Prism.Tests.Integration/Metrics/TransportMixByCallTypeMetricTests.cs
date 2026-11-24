using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The call type mix queries against a real PostgreSQL holding tables shaped like the trip table and the ledger.</summary>
public sealed class TransportMixByCallTypeMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", "Scene Response", "AETNA", "Insurance", "2026-10-03", 100, Rotor),
        new(2, "Rotor Wing", "Scene Response", "MEDICARE", "Medicare", "2026-10-04", 50, Rotor),
        new(3, "Rotor Wing", "Interfacility Transfer", "OHP", "Medicaid MCO", "2026-10-05", 30, FixedLegacy),
        new(4, "Fixed Wing", "Interfacility", "AETNA", "Insurance", "2026-10-06", 200, FixedLegacy),
        new(5, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-07", null, Rotor),
        new(6, "ALS", "Emergency", "AETNA", "Insurance", "2026-10-08", 10, Rotor),
        new(7, "ALS", "Non-Emergency", "MEDICARE", "Medicare", "2026-10-09", 5, FixedLegacy),
        new(8, "Rotor Wing", "Scene Response", "AETNA", "Insurance", "2026-09-10", 80, Rotor),
        new(9, "Rotor Wing", "Interfacility Transfer", "AETNA", "Insurance", "2026-09-11", 40, FixedLegacy),
        new(10, "Rotor Wing", "Interfacility Transfer", "MEDICARE", "Medicare", "2026-09-12", 60, FixedLegacy),
        new(11, "Rotor Wing", "Scene Response", "MEDICARE", "Medicare", "2026-10-04", 5000, "Other Provider - Base", Provider: 777),
        new(1, "Rotor Wing", "Scene Response", "MEDICARE", "Medicare", "2026-10-03", 7777, Rotor, Load: "2026-10-01"),
        new(12, "Rotor Wing", "Scene Response", "AETNA", "Insurance", "2026-11-01", 999, Rotor),
    ];

    private static readonly SeedCash[] Cash =
    [
        new(1, 1, 1, 100000),
        new(2, 3, 1, 50000),
        new(3, 8, 2, 30000),
        new(4, 9, 3, -2000),
        new(5, 2, 1, 99900, "Old AR Import"),
        new(6, 11, 9, 500000),
        new(7, 6, 1, 10000),
    ];

    private async Task<TransportMixByCallTypeMetric> NewMetricAsync() =>
        new(await CreateAsync(postgres, Trips, Cash, TestContext.Current.CancellationToken), new FakeProviderResolver(null));

    private static (string Key, int Transports, int Prior)[] Counts(TransportMixByCallType mix) => [.. mix.CallTypes.Select(c => (c.Key, c.Transports, c.PriorTransports))];

    [Fact]
    public async Task Air_transports_miles_and_payers_are_counted_by_call_type_for_the_month_and_the_one_before()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("transport_mix_by_call_type", mix.Metric.Key);
        Assert.Equal([("scene", 2, 1), ("interfacility", 2, 2), ("not-recorded", 1, 0)], Counts(mix));
        Assert.Equal([(150m, 80m), (230m, 100m), (0m, 0m)], mix.CallTypes.Select(c => (c.LoadedMiles, c.PriorLoadedMiles)));
        CallTypeRow scene = mix.CallTypes[0];
        Assert.Equal([("commercial", 1, 1), ("medicare", 1, 0)], scene.Payers.Select(p => (p.Key, p.Transports, p.PriorTransports)));
    }

    [Fact]
    public async Task Net_cash_by_call_type_is_by_posting_month_with_the_adjustment_period_and_without_the_import_or_another_provider()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        // Scene: 1 posted $1,000 in October, 8 posted $300 in September. The import (2) and the other Provider's cash (11) are out.
        Assert.Equal((1000m, 300m), (mix.CallTypes[0].NetCash, mix.CallTypes[0].PriorNetCash));
        // Interfacility: 3 posted $500 and 9 a $20 refund in the October adjustment period, whatever month they were flown.
        Assert.Equal((480m, 0m), (mix.CallTypes[1].NetCash, mix.CallTypes[1].PriorNetCash));
    }

    [Fact]
    public async Task The_trend_has_one_point_per_month_with_zeros_for_months_without_transports()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType six = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);
        TransportMixByCallType twelve = await metric.GetMixAsync(2026, Oct, 12, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(6, six.Trend.Count);
        Assert.Equal(12, twelve.Trend.Count);
        Assert.Equal(new ApPeriod(2026, 5), six.Trend[0].Period);
        Assert.Equal(Oct, six.Trend[^1].Period);
        Assert.Equal([0, 0, 0], six.Trend[0].Counts.Select(c => c.Transports));
        Assert.Equal([1, 2, 0], six.Trend[4].Counts.Select(c => c.Transports));
        Assert.Equal([2, 2, 1], six.Trend[5].Counts.Select(c => c.Transports));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(500)]
    public async Task A_months_value_that_is_not_6_or_12_never_reaches_a_query(int asked)
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, asked, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(6, mix.Months);
        Assert.Equal(6, mix.Trend.Count);
    }

    [Theory]
    [InlineData(ServiceLineFilter.Ground, new[] { "emergency", "non-emergency" })]
    [InlineData(ServiceLineFilter.Both, new[] { "scene", "interfacility", "emergency", "non-emergency", "not-recorded" })]
    public async Task The_service_line_decides_which_call_types_appear_and_they_come_in_a_fixed_order(ServiceLineFilter line, string[] keys)
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, line, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(keys, mix.CallTypes.Select(c => c.Key));
    }

    [Fact]
    public async Task A_group_key_narrows_it_to_one_aircraft_type()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Air, "fixed-wing", null, TestContext.Current.CancellationToken);

        Assert.Equal([("interfacility", 1, 0)], Counts(mix));
    }

    private static async Task<int> TotalAsync(TransportMixByCallTypeMetric metric, ReportingDivision? division)
    {
        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Both, null, division, TestContext.Current.CancellationToken);
        return mix.CallTypes.Sum(c => c.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        int all = await TotalAsync(metric, null);
        int rotor = await TotalAsync(metric, RotorDivision);
        int fixedTrips = await TotalAsync(metric, FixedDivision);

        Assert.True(rotor > 0);
        Assert.True(fixedTrips > 0);
        Assert.Equal(all, rotor + fixedTrips);
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("x' OR '1'='1")]
    [InlineData("")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }

    [Fact]
    public async Task Another_providers_trips_an_older_load_and_other_months_are_never_counted()
    {
        TransportMixByCallTypeMetric metric = await NewMetricAsync();

        TransportMixByCallType mix = await metric.GetMixAsync(2026, Oct, 6, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(230m + 150m, mix.CallTypes.Sum(c => c.LoadedMiles));
        Assert.Equal(5, mix.CallTypes.Sum(c => c.Transports));
    }
}
