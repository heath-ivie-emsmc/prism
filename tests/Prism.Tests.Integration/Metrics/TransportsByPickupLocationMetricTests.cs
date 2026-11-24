using System.Data.Common;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The pickup location queries against a real PostgreSQL holding tables shaped like the trip table (with PHI markers in the origin address columns) and the ledger.</summary>
public sealed class TransportsByPickupLocationMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, Rotor, Facility: "HOME FACILITY S", Mod: "S"),
        new(2, "Rotor Wing", null, "MEDICARE", "Medicare", "2026-10-04", 10, Rotor, Facility: "HOME FACILITY R", Mod: " r "),
        new(3, "Rotor Wing", null, "OHP", "Medicaid MCO", "2026-10-05", 30, FixedLegacy, Facility: "MERCY HOSPITAL", Mod: "H"),
        new(4, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-06", 40, FixedLegacy, Facility: "MERCY HOSPITAL", Mod: "h"),
        new(5, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-07", 5, FixedLegacy, Facility: "SUNNY SNF", Mod: "N"),
        new(6, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-08", 6, FixedLegacy, Facility: "DIALYSIS ONE", Mod: "G"),
        new(7, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-09", 7, FixedLegacy, Facility: "DIALYSIS TWO", Mod: "J"),
        new(8, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-10", 1, Rotor, Facility: "SS FAC", Mod: "SS"),
        new(9, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-11", 1, Rotor, Facility: "NULL MOD FAC", Mod: null),
        new(10, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-12", 1, Rotor, Facility: "BLANK MOD FAC", Mod: ""),
        new(11, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-13", 1, Rotor, Facility: "X MOD FAC", Mod: "X"),
        new(12, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-14", 2, Rotor, Facility: "CUSTODIAL HOME", Mod: "E"),
        new(13, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-03", 8, FixedLegacy, Facility: "MERCY HOSPITAL", Mod: "H"),
        new(14, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-04", 9, Rotor, Facility: "HOME FACILITY S", Mod: "S"),
        new(15, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-05", 4, FixedLegacy, Facility: "OLD HOSP", Mod: "H"),
        new(20, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Facility: "OTHER PROV HOSP", Mod: "H", Provider: 777),
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 7777, Rotor, Facility: "OLD LOAD HOSP", Mod: "H", Load: "2026-10-01"),
        new(30, "Rotor Wing", null, "AETNA", "Insurance", "2026-11-02", 999, Rotor, Facility: "NOVEMBER HOSP", Mod: "H"),
    ];

    private static readonly SeedCash[] Cash =
    [
        new(1, 3, 1, 50000),
        new(2, 13, 2, 30000),
        new(3, 1, 1, 10000),
        new(4, 2, 1, 99900, "Old AR Import"),
        new(5, 20, 9, 500000),
    ];

    private sealed class Recording(IWarehouse inner) : IWarehouse
    {
        public List<string> Sql { get; } = [];

        public Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken)
        {
            Sql.Add(query.Sql);
            return inner.QueryAsync(query, map, cancellationToken);
        }
    }

    private async Task<(TransportsByPickupLocationMetric Metric, Recording Warehouse)> NewAsync(IEnumerable<SeedTrip>? trips = null)
    {
        var warehouse = new Recording(await CreateAsync(postgres, trips ?? Trips, trips is null ? Cash : [], TestContext.Current.CancellationToken));
        return (new TransportsByPickupLocationMetric(warehouse, new FakeProviderResolver(null)), warehouse);
    }

    private static Task<TransportsByPickupLocation> AirAsync(TransportsByPickupLocationMetric metric, string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
        metric.GetPickupsAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Pickups_are_bucketed_by_the_origin_modifier_and_anything_else_is_other()
    {
        (TransportsByPickupLocationMetric metric, _) = await NewAsync();

        TransportsByPickupLocation result = await AirAsync(metric);

        Assert.Equal("transports_by_pickup_location", result.Metric.Key);
        // S, a padded lower-case r and E are single buckets. G and J are one dialysis bucket. A two-letter, null, blank or unknown modifier is Other. Another Provider, an older load and November are out.
        Assert.Equal(
            [("scene", 1, 1), ("residence", 1, 0), ("custodial-facility", 1, 0), ("hospital", 2, 2), ("skilled-nursing", 1, 0), ("dialysis", 2, 0), ("other", 4, 0)],
            result.Sources.Select(s => (s.Key, s.Transports, s.PriorTransports)));
        Assert.Equal([false, false, false, true, true, true, false], result.Sources.Select(s => s.FacilityBased));
        Assert.Equal((100m, 9m), (result.Sources[0].LoadedMiles, result.Sources[0].PriorLoadedMiles));
    }

    [Fact]
    public async Task Each_source_has_its_payer_mix_and_posting_month_net_cash()
    {
        (TransportsByPickupLocationMetric metric, _) = await NewAsync();

        TransportsByPickupLocation result = await AirAsync(metric);

        PickupSource hospital = result.Sources.Single(s => s.Key == "hospital");
        Assert.Equal([("commercial", 1, 2), ("medicaid", 1, 0)], hospital.Payers.Select(p => (p.Key, p.Transports, p.PriorTransports)));
        Assert.Equal((500m, 300m), (hospital.NetCash, hospital.PriorNetCash));
        // The import (encounter 2) and another Provider's cash (encounter 20) are not counted.
        Assert.Equal((100m, 0m), (result.Sources[0].NetCash, result.Sources[0].PriorNetCash));
        Assert.Equal(0m, result.Sources.Single(s => s.Key == "residence").NetCash);
    }

    [Fact]
    public async Task Only_facility_based_sources_list_facilities_and_scene_residence_custodial_and_other_never_do()
    {
        (TransportsByPickupLocationMetric metric, _) = await NewAsync();

        TransportsByPickupLocation result = await AirAsync(metric);

        Assert.Equal(
            [("hospital", "MERCY HOSPITAL", 2, 1), ("dialysis", "DIALYSIS ONE", 1, 0), ("dialysis", "DIALYSIS TWO", 1, 0), ("skilled-nursing", "SUNNY SNF", 1, 0), ("hospital", "OLD HOSP", 0, 1)],
            result.Facilities.Select(f => (f.SourceKey, f.Name, f.Transports, f.PriorTransports)));
        string[] hidden = ["HOME FACILITY S", "HOME FACILITY R", "CUSTODIAL HOME", "SS FAC", "NULL MOD FAC", "BLANK MOD FAC", "X MOD FAC", PhiMarker];
        Assert.DoesNotContain(result.Facilities, f => hidden.Contains(f.Name));
    }

    [Fact]
    public async Task The_facility_list_is_the_top_20_across_the_whole_result_plus_one_other_row_that_keeps_the_totals()
    {
        SeedTrip[] many = [.. Enumerable.Range(1, 25).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: $"HOSP {i:D2}", Mod: i % 2 == 0 ? "H" : "N"))];
        (TransportsByPickupLocationMetric metric, _) = await NewAsync(many);

        TransportsByPickupLocation result = await AirAsync(metric);

        Assert.Equal(21, result.Facilities.Count);
        PickupFacility other = result.Facilities[^1];
        Assert.Equal((null, "Other", true, 5), (other.SourceKey, other.Name, other.IsOther, other.Transports));
        Assert.Equal(25, result.Facilities.Sum(f => f.Transports));
    }

    [Fact]
    public async Task Hostile_facility_text_is_cleaned_cut_and_never_a_control_character_or_the_roll_up_row()
    {
        SeedTrip[] hostile =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "=HYPERLINK(\"http://evil\",\"x\")", Mod: "H"),
            new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: new string('z', 500), Mod: "H"),
            new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "A\tB\r\nC\u0001D", Mod: "H"),
            new(4, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: null, Mod: "H"),
            new(5, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "    ", Mod: "H"),
            new(6, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "Other", Mod: "H"),
        ];
        (TransportsByPickupLocationMetric metric, _) = await NewAsync(hostile);

        TransportsByPickupLocation result = await AirAsync(metric);

        string[] names = [.. result.Facilities.Select(f => f.Name)];
        Assert.Contains("=HYPERLINK(\"http://evil\",\"x\")", names);
        Assert.Contains(new string('z', 120), names);
        Assert.Contains("A B C D", names);
        Assert.Contains("Other facility", names);
        // A null and a blank facility are one Not recorded entry.
        Assert.Equal(2, result.Facilities.Single(f => f.Name == "Not recorded").Transports);
        Assert.All(names, n => Assert.True(n.Length <= 120 && !n.Any(char.IsControl)));
        Assert.DoesNotContain(result.Facilities, f => f.IsOther);
    }

    [Fact]
    public async Task No_query_ever_selects_an_origin_address_city_state_or_zip_or_a_patient_column()
    {
        (TransportsByPickupLocationMetric metric, Recording warehouse) = await NewAsync();

        _ = await AirAsync(metric);

        string[] forbidden = ["origin_address", "origin_city", "origin_state", "origin_zip", "patient_", "first_name", "last_name", "birth", "ssn", "narrative", "dob"];
        Assert.Equal(3, warehouse.Sql.Count);
        Assert.All(warehouse.Sql, sql => Assert.All(forbidden, word => Assert.DoesNotContain(word, sql, StringComparison.OrdinalIgnoreCase)));
        Assert.All(warehouse.Sql, sql => Assert.Contains("origin_mod", sql, StringComparison.Ordinal));
    }

    private static async Task<int> TotalAsync(TransportsByPickupLocationMetric metric, ReportingDivision? division) =>
        (await AirAsync(metric, division: division)).Sources.Sum(s => s.Transports);

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        (TransportsByPickupLocationMetric metric, _) = await NewAsync();

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
        (TransportsByPickupLocationMetric metric, _) = await NewAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }

    [Fact]
    public async Task The_service_line_and_the_group_key_narrow_the_figures()
    {
        SeedTrip[] trips =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "H1", Mod: "H"),
            new(2, "Fixed Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "H2", Mod: "H"),
            new(3, "ALS", null, "A", "Insurance", "2026-10-03", 1, Rotor, Facility: "H3", Mod: "H"),
        ];
        (TransportsByPickupLocationMetric metric, _) = await NewAsync(trips);

        TransportsByPickupLocation ground = await AirAsync(metric, line: ServiceLineFilter.Ground);
        TransportsByPickupLocation fixedWing = await AirAsync(metric, group: "fixed-wing");

        Assert.Equal(["H3"], ground.Facilities.Select(f => f.Name));
        Assert.Equal(["H2"], fixedWing.Facilities.Select(f => f.Name));
    }
}
