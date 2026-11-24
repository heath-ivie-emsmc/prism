using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The Reporting Division analysis query against a real PostgreSQL holding tables shaped like the trip table and the ledger.</summary>
public sealed class BaseAnalysisMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, Rotor, Vehicle: "N123AB"),
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

    private sealed class Directory(params ReportingDivision[] divisions) : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReportingDivision>>(divisions);
    }

    private async Task<BaseAnalysisMetric> NewMetricAsync(IEnumerable<SeedTrip>? trips = null) =>
        new(await CreateAsync(postgres, trips ?? Trips, trips is null ? Cash : [], TestContext.Current.CancellationToken), new FakeProviderResolver(null), new Directory(RotorDivision, FixedDivision));

    private static Task<BaseAnalysis> AirAsync(BaseAnalysisMetric metric, string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
        metric.GetAnalysisAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Figures_are_counted_by_division_with_the_legacy_suffix_folded_into_the_current_division()
    {
        BaseAnalysisMetric metric = await NewMetricAsync();

        BaseAnalysis result = await AirAsync(metric);

        Assert.Equal("base_analysis", result.Metric.Key);
        Assert.Equal([("rotor", Rotor), ("fixed", Fixed)], result.Divisions.Select(d => (d.Key, d.Name)));
        Assert.Equal([(3, 1, 150m, 80m, 1000m, 300m), (2, 2, 230m, 100m, 480m, 0m)],
            result.Divisions.Select(d => (d.Figures.Transports, d.Figures.PriorTransports, d.Figures.LoadedMiles, d.Figures.PriorLoadedMiles, d.Figures.NetCash, d.Figures.PriorNetCash)));
        Assert.Equal(5, result.Totals.Transports);
        Assert.Equal(1000m / 3, result.Divisions[0].Figures.NetCashPerTransport);
    }

    [Fact]
    public async Task Each_division_lists_the_tails_operating_from_it_with_their_transport_counts()
    {
        BaseAnalysisMetric metric = await NewMetricAsync();

        BaseAnalysis result = await AirAsync(metric);

        Assert.Equal([("N123AB", 2), ("Not recorded", 1)], result.Divisions[0].Aircraft.Select(a => (a.Tail, a.Transports)));
        Assert.Equal([("N456CD", 1), ("N789EF", 1)], result.Divisions[1].Aircraft.Select(a => (a.Tail, a.Transports)));
    }

    [Fact]
    public async Task A_division_the_directory_does_not_hold_has_no_key_and_trips_with_no_division_are_not_recorded()
    {
        SeedTrip[] trips =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, "Brand New Base", Vehicle: "T1"),
            new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, "<none>", Vehicle: "T2"),
            new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, "  ", Vehicle: "T3"),
        ];
        BaseAnalysisMetric metric = await NewMetricAsync(trips);

        BaseAnalysis result = await AirAsync(metric);

        Assert.Equal([(null, "Brand New Base", 1), (null, "Not recorded", 2)], result.Divisions.Select(d => (d.Key, d.Name, d.Figures.Transports)).OrderBy(d => d.Name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_tails_of_a_division_are_the_top_10_plus_one_other_entry_and_the_divisions_the_top_100_plus_other()
    {
        SeedTrip[] tails = [.. Enumerable.Range(1, 12).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 10, Rotor, Vehicle: i == 12 ? "Other" : $"T{i:D2}"))];
        BaseAnalysisMetric tailMetric = await NewMetricAsync(tails);

        BaseAnalysis byTail = await AirAsync(tailMetric);

        DivisionRow row = Assert.Single(byTail.Divisions);
        Assert.Equal(11, row.Aircraft.Count);
        Assert.Equal(("Other", true, 2), (row.Aircraft[^1].Tail, row.Aircraft[^1].IsOther, row.Aircraft[^1].Transports));
        Assert.Equal(12, row.Aircraft.Sum(a => a.Transports));

        SeedTrip[] bases = [.. Enumerable.Range(1, 105).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 10, $"Base {i:D3}", Vehicle: "T1"))];
        BaseAnalysisMetric baseMetric = await NewMetricAsync(bases);

        BaseAnalysis byBase = await AirAsync(baseMetric);

        Assert.Equal(101, byBase.Divisions.Count);
        DivisionRow other = byBase.Divisions[^1];
        Assert.Equal((true, 5, 0), (other.IsOther, other.Figures.Transports, other.Aircraft.Count));
        Assert.Equal(105, byBase.Totals.Transports);
    }

    [Fact]
    public async Task A_group_key_and_the_service_line_narrow_the_figures()
    {
        BaseAnalysisMetric metric = await NewMetricAsync();

        BaseAnalysis fixedWing = await AirAsync(metric, group: "fixed-wing");
        BaseAnalysis ground = await AirAsync(metric, line: ServiceLineFilter.Ground);

        Assert.Equal([("fixed", 1)], fixedWing.Divisions.Select(d => (d.Key, d.Figures.Transports)));
        Assert.Equal([("rotor", 1)], ground.Divisions.Select(d => (d.Key, d.Figures.Transports)));
    }

    private static async Task<int> TotalAsync(BaseAnalysisMetric metric, ReportingDivision? division) =>
        (await AirAsync(metric, division: division, line: ServiceLineFilter.Both)).Totals.Transports;

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        BaseAnalysisMetric metric = await NewMetricAsync();

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
        BaseAnalysisMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
