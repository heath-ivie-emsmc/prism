using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The loaded miles by tail number query against a real PostgreSQL holding a table shaped like the trip table.</summary>
public sealed class LoadedMilesByAircraftMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 100, Rotor, Vehicle: "  N123AB "),
        new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-04", 50, Rotor, Vehicle: "N123AB"),
        new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-05", 30, FixedLegacy, Vehicle: "N456CD"),
        new(4, "Fixed Wing", null, "A", "Insurance", "2026-10-06", 200, FixedLegacy, Vehicle: "N789EF"),
        new(5, "Rotor Wing", null, "A", "Insurance", "2026-10-07", null, Rotor, Vehicle: null),
        new(6, "ALS", null, "A", "Insurance", "2026-10-08", 10, Rotor, Vehicle: "MEDIC 1"),
        new(7, "Rotor Wing", null, "A", "Insurance", "2026-09-10", 80, Rotor, Vehicle: "N123AB"),
        new(8, "Rotor Wing", null, "A", "Insurance", "2026-09-11", 40, FixedLegacy, Vehicle: "N456CD"),
        new(9, "Rotor Wing", null, "A", "Insurance", "2026-09-12", 60, FixedLegacy, Vehicle: "N456CD"),
        new(20, "Rotor Wing", null, "A", "Insurance", "2026-07-10", 40, Rotor, Vehicle: "IDLE1"),
        new(22, "Rotor Wing", null, "A", "Insurance", "2025-12-01", 10, Rotor, Vehicle: "N123AB"),
        new(11, "Rotor Wing", null, "A", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Vehicle: "N999ZZ", Provider: 777),
        new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 7777, Rotor, Vehicle: "N123AB", Load: "2026-10-01"),
        new(30, "Rotor Wing", null, "A", "Insurance", "2026-11-02", 999, Rotor, Vehicle: "N123AB"),
    ];

    private async Task<LoadedMilesByAircraftMetric> NewMetricAsync(IEnumerable<SeedTrip>? trips = null) =>
        new(await CreateAsync(postgres, trips ?? Trips, [], TestContext.Current.CancellationToken), new FakeProviderResolver(null));

    private static Task<LoadedMilesByAircraft> AirAsync(LoadedMilesByAircraftMetric metric, int months = 6, string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
        metric.GetMilesAsync(2026, Oct, months, line, group, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Tails_are_ranked_by_the_miles_of_the_month_with_transports_and_the_average_per_transport()
    {
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

        LoadedMilesByAircraft result = await AirAsync(metric);

        Assert.Equal("loaded_miles_by_aircraft", result.Metric.Key);
        // N789EF 200, N123AB 150 (a padded tail is the same tail), N456CD 30, then the two with no miles in October: IDLE1 flew earlier (more miles in the window), Not recorded did not.
        Assert.Equal(["N789EF", "N123AB", "N456CD", "IDLE1", "Not recorded"], result.Aircraft.Select(a => a.Tail));
        Assert.Equal([(1, 200m, 200m), (2, 150m, 75m), (1, 30m, 30m), (0, 0m, (decimal?)null), (1, 0m, 0m)],
            result.Aircraft.Select(a => (a.Transports, a.LoadedMiles, a.AverageLoadedMiles)));
    }

    [Fact]
    public async Task The_trend_has_one_point_per_month_with_zeros_and_a_null_average_for_months_without_transports()
    {
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

        LoadedMilesByAircraft six = await AirAsync(metric);
        LoadedMilesByAircraft twelve = await AirAsync(metric, months: 12);

        AircraftMilesRow n123 = six.Aircraft.Single(a => a.Tail == "N123AB");
        Assert.Equal(6, n123.Trend.Count);
        Assert.Equal(new ApPeriod(2026, 5), n123.Trend[0].Period);
        Assert.Equal([(0, 0m, (decimal?)null), (0, 0m, null), (0, 0m, null), (0, 0m, null), (1, 80m, 80m), (2, 150m, 75m)], n123.Trend.Select(p => (p.Transports, p.LoadedMiles, p.AverageLoadedMiles)));
        AircraftMilesRow idle = six.Aircraft.Single(a => a.Tail == "IDLE1");
        Assert.Equal((1, 40m), (idle.Trend[2].Transports, idle.Trend[2].LoadedMiles));
        // December 2025 is outside six months and inside twelve.
        Assert.Equal(12, twelve.Aircraft[0].Trend.Count);
        Assert.Equal((1, 10m), (twelve.Aircraft.Single(a => a.Tail == "N123AB").Trend[1].Transports, twelve.Aircraft.Single(a => a.Tail == "N123AB").Trend[1].LoadedMiles));
        Assert.DoesNotContain(six.Aircraft.SelectMany(a => a.Trend), p => p.LoadedMiles is 999m or 5000m or 7777m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(500)]
    public async Task A_months_value_that_is_not_6_or_12_never_reaches_a_query(int asked)
    {
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

        LoadedMilesByAircraft result = await AirAsync(metric, months: asked);

        Assert.Equal(6, result.Months);
        Assert.All(result.Aircraft, a => Assert.Equal(6, a.Trend.Count));
    }

    [Fact]
    public async Task The_service_line_and_the_group_key_narrow_the_tails()
    {
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

        LoadedMilesByAircraft ground = await AirAsync(metric, line: ServiceLineFilter.Ground);
        LoadedMilesByAircraft fixedWing = await AirAsync(metric, group: "fixed-wing");

        Assert.Equal(["MEDIC 1"], ground.Aircraft.Select(a => a.Tail));
        Assert.Equal(["N789EF"], fixedWing.Aircraft.Select(a => a.Tail));
    }

    [Fact]
    public async Task The_tail_list_is_bounded_to_the_top_100_plus_one_other_row_with_its_own_trend()
    {
        List<SeedTrip> many = [.. Enumerable.Range(1, 105).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", i, Rotor, Vehicle: $"T{i:D3}"))];
        many.Add(new SeedTrip(500, "Rotor Wing", null, "A", "Insurance", "2026-09-03", 7, Rotor, Vehicle: "T001"));
        LoadedMilesByAircraftMetric metric = await NewMetricAsync(many);

        LoadedMilesByAircraft result = await AirAsync(metric);

        Assert.Equal(101, result.Aircraft.Count);
        Assert.Equal("T105", result.Aircraft[0].Tail);
        AircraftMilesRow other = result.Aircraft[^1];
        Assert.Equal(("Other", true, 5, 15m), (other.Tail, other.IsOther, other.Transports, other.LoadedMiles));
        Assert.Equal((1, 7m), (other.Trend[4].Transports, other.Trend[4].LoadedMiles));
        Assert.Equal(106, result.Aircraft.Sum(a => a.Trend.Sum(p => p.Transports)));
    }

    [Fact]
    public async Task Hostile_tail_text_is_cleaned_and_cut()
    {
        SeedTrip[] hostile =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 3, Rotor, Vehicle: "A\tB\r\nC"),
            new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 2, Rotor, Vehicle: new string('y', 500)),
            new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "Other"),
        ];
        LoadedMilesByAircraftMetric metric = await NewMetricAsync(hostile);

        LoadedMilesByAircraft result = await AirAsync(metric);

        Assert.Equal(["A B C", new string('y', 120), "Other tail"], result.Aircraft.Select(a => a.Tail));
        Assert.DoesNotContain(result.Aircraft, a => a.IsOther);
    }

    private static async Task<int> TotalAsync(LoadedMilesByAircraftMetric metric, ReportingDivision? division) =>
        (await AirAsync(metric, division: division, line: ServiceLineFilter.Both)).Aircraft.Sum(a => a.Transports);

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

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
        LoadedMilesByAircraftMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
