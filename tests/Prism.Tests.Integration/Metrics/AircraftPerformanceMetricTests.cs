using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The tail number performance query against a real PostgreSQL holding tables shaped like the trip table and the ledger.</summary>
public sealed class AircraftPerformanceMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, Rotor, Vehicle: "  N123AB "),
        new(2, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 50, Rotor, Vehicle: "N123AB"),
        new(3, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-05", 30, FixedLegacy, Vehicle: "N456CD"),
        new(4, "Fixed Wing", null, "AETNA", "Insurance", "2026-10-06", 200, FixedLegacy, Vehicle: "N789EF"),
        new(5, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-07", null, Rotor, Vehicle: null),
        new(6, "ALS", null, "AETNA", "Insurance", "2026-10-08", 10, Rotor, Vehicle: "MEDIC 1"),
        new(7, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-10", 80, Rotor, Vehicle: "N123AB"),
        new(8, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-11", 40, FixedLegacy, Vehicle: "N456CD"),
        new(9, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-12", 60, FixedLegacy, Vehicle: "N456CD"),
        new(11, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Vehicle: "N999ZZ", Provider: 777),
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 7777, Rotor, Vehicle: "N123AB", Load: "2026-10-01"),
    ];

    private static readonly SeedCash[] Cash =
    [
        new(1, 1, 1, 100000),
        new(2, 3, 1, 50000),
        new(3, 7, 2, 30000),
        new(4, 8, 3, -2000),
        new(5, 2, 1, 99900, "Old AR Import"),
        new(6, 11, 9, 500000),
        new(7, 6, 1, 10000),
    ];

    private async Task<AircraftPerformanceMetric> NewMetricAsync(IEnumerable<SeedTrip>? trips = null) =>
        new(await CreateAsync(postgres, trips ?? Trips, trips is null ? Cash : [], TestContext.Current.CancellationToken), new FakeProviderResolver(null));

    private static Task<AircraftPerformance> AirAsync(AircraftPerformanceMetric metric, string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
        metric.GetPerformanceAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Transports_miles_and_net_cash_are_counted_by_tail_for_the_month_and_the_one_before()
    {
        AircraftPerformanceMetric metric = await NewMetricAsync();

        AircraftPerformance result = await AirAsync(metric);

        Assert.Equal("aircraft_performance", result.Metric.Key);
        // Largest first. A padded tail counts with the same tail, and a missing tail is Not recorded. Another Provider's tail and an older load are out.
        Assert.Equal(["N123AB", "N456CD", "N789EF", "Not recorded"], result.Aircraft.Select(a => a.Tail));
        Assert.Equal([(2, 1, 150m, 80m, 1000m, 300m), (1, 2, 30m, 100m, 480m, 0m), (1, 0, 200m, 0m, 0m, 0m), (1, 0, 0m, 0m, 0m, 0m)],
            result.Aircraft.Select(a => (a.Figures.Transports, a.Figures.PriorTransports, a.Figures.LoadedMiles, a.Figures.PriorLoadedMiles, a.Figures.NetCash, a.Figures.PriorNetCash)));
        Assert.DoesNotContain(result.Aircraft, a => a.IsOther);
    }

    [Fact]
    public async Task Net_cash_per_transport_is_posting_month_cash_over_transports_and_null_without_transports()
    {
        AircraftPerformanceMetric metric = await NewMetricAsync();

        AircraftPerformance result = await AirAsync(metric);

        Assert.Equal((500m, 300m), (result.Aircraft[0].Figures.NetCashPerTransport, result.Aircraft[0].Figures.PriorNetCashPerTransport));
        Assert.Equal((480m, 0m), (result.Aircraft[1].Figures.NetCashPerTransport, result.Aircraft[1].Figures.PriorNetCashPerTransport));
        // N789EF flew nothing in September, so there is no prior figure to show. It is null, never zero.
        Assert.Equal((0m, (decimal?)null), (result.Aircraft[2].Figures.NetCashPerTransport, result.Aircraft[2].Figures.PriorNetCashPerTransport));
    }

    [Fact]
    public async Task The_totals_add_up_every_row_and_the_service_line_decides_which_tails_count()
    {
        AircraftPerformanceMetric metric = await NewMetricAsync();

        AircraftPerformance air = await AirAsync(metric);
        AircraftPerformance both = await AirAsync(metric, line: ServiceLineFilter.Both);
        AircraftPerformance ground = await AirAsync(metric, line: ServiceLineFilter.Ground);

        Assert.Equal((5, 3), (air.Totals.Transports, air.Totals.PriorTransports));
        Assert.Equal(air.Aircraft.Sum(a => a.Figures.NetCash), air.Totals.NetCash);
        Assert.Contains(both.Aircraft, a => a.Tail == "MEDIC 1");
        Assert.Equal(["MEDIC 1"], ground.Aircraft.Select(a => a.Tail));
        Assert.Equal(100m, ground.Aircraft[0].Figures.NetCash);
    }

    [Fact]
    public async Task A_group_key_narrows_it_to_one_aircraft_type()
    {
        AircraftPerformanceMetric metric = await NewMetricAsync();

        AircraftPerformance result = await AirAsync(metric, group: "fixed-wing");

        Assert.Equal(["N789EF"], result.Aircraft.Select(a => a.Tail));
    }

    private static async Task<int> TotalAsync(AircraftPerformanceMetric metric, ReportingDivision? division) =>
        (await AirAsync(metric, division: division, line: ServiceLineFilter.Both)).Totals.Transports;

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        AircraftPerformanceMetric metric = await NewMetricAsync();

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
        AircraftPerformanceMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }

    [Fact]
    public async Task Hostile_tail_text_is_cleaned_cut_and_never_a_formula_or_control_character_in_the_row()
    {
        SeedTrip[] hostile =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "A\tB\r\nC\u0001D"),
            new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: new string('x', 500)),
            new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "   "),
            new(4, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "Other"),
            new(5, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "=HYPERLINK(\"http://evil\")"),
        ];
        AircraftPerformanceMetric metric = await NewMetricAsync(hostile);

        AircraftPerformance result = await AirAsync(metric);

        string[] tails = [.. result.Aircraft.Select(a => a.Tail)];
        Assert.Contains("A B C D", tails);
        Assert.Contains(new string('x', 120), tails);
        Assert.Contains("Not recorded", tails);
        Assert.Contains("Other tail", tails);
        Assert.Contains("=HYPERLINK(\"http://evil\")", tails);
        Assert.All(tails, t => Assert.True(t.Length <= 120 && !t.Any(char.IsControl)));
        Assert.DoesNotContain(result.Aircraft, a => a.IsOther);
    }

    [Fact]
    public async Task The_tail_list_is_bounded_to_the_top_100_plus_one_other_row_that_keeps_the_totals()
    {
        SeedTrip[] many = [.. Enumerable.Range(1, 105).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 10, Rotor, Vehicle: $"T{i:D3}"))];
        AircraftPerformanceMetric metric = await NewMetricAsync(many);

        AircraftPerformance result = await AirAsync(metric);

        Assert.Equal(101, result.Aircraft.Count);
        AircraftRow other = result.Aircraft[^1];
        Assert.Equal(("Other", true, 5, 50m), (other.Tail, other.IsOther, other.Figures.Transports, other.Figures.LoadedMiles));
        Assert.Equal(105, result.Totals.Transports);
        Assert.Equal(1050m, result.Totals.LoadedMiles);
    }
}
