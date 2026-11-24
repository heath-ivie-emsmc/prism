using System.Text.Json;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// The three contribution queries against a real PostgreSQL holding tables shaped like the trip table and the ledger. The point of every test is that the contributions add
/// up to the change in net cash, whatever the cap, the filters and the hostile text.
/// </summary>
public sealed class CashContributionMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    // Air in October: enc 1 (N123AB, Rotor, commercial) 1000 + 50 adjustment, enc 3 (N789EF, Fixed, medicaid) 500, enc 4 (N456CD, Fixed, commercial) 100 against 400 in September,
    // enc 6 (no tail, Rotor, facility) -20, enc 8 (N999, no division, no payer category) 10. September also has enc 2 (N123AB, Rotor, medicare) 300.
    // October 1640, September 700, change 940. Ground: enc 5 (MEDIC 1, patient) 200. An Old AR import, another Provider and an older load must not count.
    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, Rotor, Vehicle: "  N123AB "),
        new(2, "Rotor Wing", null, "MEDICARE", "Medicare", "2026-09-04", 50, Rotor, Vehicle: "N123AB"),
        new(3, "Fixed Wing", null, "MEDICAID", "Medicaid", "2026-10-05", 30, FixedLegacy, Vehicle: "N789EF"),
        new(4, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-06", 200, Fixed, Vehicle: "N456CD"),
        new(5, "ALS", null, "SELF", "Patient", "2026-10-08", 10, Rotor, Vehicle: "MEDIC 1"),
        new(6, "Rotor Wing", null, "HOSPITAL", "Facility", "2026-10-07", null, Rotor, Vehicle: null),
        new(8, "Rotor Wing", null, "ODD", "Something Else", "2026-10-09", 5, "<none>", Vehicle: "N999"),
        new(11, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Vehicle: "N999ZZ", Provider: 777),
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 7777, Rotor, Vehicle: "N123AB", Load: "2026-10-01"),
    ];

    private static readonly SeedCash[] Cash =
    [
        new(1, 1, 1, 100000),
        new(2, 2, 2, 30000),
        new(3, 3, 1, 50000),
        new(4, 4, 2, 40000),
        new(5, 4, 1, 10000),
        new(6, 1, 3, 5000),
        new(7, 5, 1, 20000),
        new(8, 6, 1, -2000),
        new(9, 2, 1, 99900, "Old AR Import"),
        new(10, 11, 9, 500000),
        new(11, 8, 1, 1000),
    ];

    private sealed class Directory(params ReportingDivision[] divisions) : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReportingDivision>>(divisions);
    }

    private sealed record Metrics(CashContributionByAircraftMetric Aircraft, CashContributionByDivisionMetric Division, CashContributionByPayerMetric Payer, IWarehouse Warehouse)
    {
        public Task<CashContribution> AircraftAsync(string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
            Aircraft.GetContributionAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);

        public Task<CashContribution> DivisionAsync(string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
            Division.GetContributionAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);

        public Task<CashContribution> PayerAsync(string? group = null, ReportingDivision? division = null, ServiceLineFilter line = ServiceLineFilter.Air) =>
            Payer.GetContributionAsync(2026, Oct, line, group, division, TestContext.Current.CancellationToken);
    }

    private async Task<Metrics> NewAsync(IEnumerable<SeedTrip>? trips = null, IEnumerable<SeedCash>? cash = null)
    {
        IWarehouse warehouse = await CreateAsync(postgres, trips ?? Trips, cash ?? (trips is null ? Cash : []), TestContext.Current.CancellationToken);
        var providers = new FakeProviderResolver(null);
        return new Metrics(
            new CashContributionByAircraftMetric(warehouse, providers),
            new CashContributionByDivisionMetric(warehouse, providers, new Directory(RotorDivision, FixedDivision)),
            new CashContributionByPayerMetric(warehouse, providers),
            warehouse);
    }

    private static void AssertAddsUp(CashContribution result, decimal expectedNow, decimal expectedPrior)
    {
        Assert.Equal((expectedNow, expectedPrior, expectedNow - expectedPrior), (result.NetCash, result.PriorNetCash, result.Change));
        Assert.Equal(result.Change, result.Rows.Sum(r => r.Change));
        Assert.Equal(result.NetCash, result.Rows.Sum(r => r.NetCash));
        Assert.Equal(result.PriorNetCash, result.Rows.Sum(r => r.PriorNetCash));
    }

    [Fact]
    public async Task Each_tail_contributes_its_change_and_the_contributions_add_up_to_the_total_change()
    {
        Metrics metrics = await NewAsync();

        CashContribution result = await metrics.AircraftAsync();

        Assert.Equal("cash_contribution_by_aircraft", result.Metric.Key);
        // Largest absolute movers first. A padded tail counts with the same tail, the adjustment period counts in its month, and a missing tail is Not recorded.
        Assert.Equal([("N123AB", 750m), ("N789EF", 500m), ("N456CD", -300m), ("Not recorded", -20m), ("N999", 10m)], result.Rows.Select(r => (r.Name, r.Change)));
        Assert.Equal([(1050m, 300m), (500m, 0m), (100m, 400m)], result.Rows.Take(3).Select(r => (r.NetCash, r.PriorNetCash)));
        Assert.DoesNotContain(result.Rows, r => r.IsOther);
        AssertAddsUp(result, 1640m, 700m);
    }

    [Fact]
    public async Task The_service_line_decides_which_tails_count_and_the_total_still_adds_up()
    {
        Metrics metrics = await NewAsync();

        CashContribution all = await metrics.AircraftAsync(line: ServiceLineFilter.Both);
        CashContribution ground = await metrics.AircraftAsync(line: ServiceLineFilter.Ground);

        Assert.Contains(all.Rows, r => r.Name == "MEDIC 1" && r.Change == 200m);
        AssertAddsUp(all, 1840m, 700m);
        Assert.Equal([("MEDIC 1", 200m)], ground.Rows.Select(r => (r.Name, r.Change)));
        AssertAddsUp(ground, 200m, 0m);
    }

    [Fact]
    public async Task A_group_key_narrows_the_contribution_to_one_aircraft_type()
    {
        Metrics metrics = await NewAsync();

        CashContribution fixedWing = await metrics.AircraftAsync(group: "fixed-wing");
        CashContribution payers = await metrics.PayerAsync(group: "fixed-wing");

        Assert.Equal([("N789EF", 500m)], fixedWing.Rows.Select(r => (r.Name, r.Change)));
        Assert.Equal("fixed-wing", fixedWing.GroupKey);
        Assert.Equal([("medicaid", 500m)], payers.Rows.Select(r => (r.Key, r.Change)));
    }

    [Fact]
    public async Task Each_division_contributes_its_change_with_the_legacy_suffix_folded_in_and_a_missing_division_is_not_recorded()
    {
        Metrics metrics = await NewAsync();

        CashContribution result = await metrics.DivisionAsync();

        Assert.Equal("cash_contribution_by_division", result.Metric.Key);
        Assert.Equal([("rotor", Rotor, 730m), ("fixed", Fixed, 200m), (null, "Not recorded", 10m)], result.Rows.Select(r => (r.Key, r.Name, r.Change)));
        AssertAddsUp(result, 1640m, 700m);
    }

    [Fact]
    public async Task Each_payer_group_contributes_its_change_and_only_group_keys_and_fixed_names_leave_the_query()
    {
        Metrics metrics = await NewAsync();

        CashContribution result = await metrics.PayerAsync();

        Assert.Equal("cash_contribution_by_payer", result.Metric.Key);
        Assert.Equal([("commercial", "Commercial", 750m), ("medicaid", "Medicaid", 500m), ("medicare", "Medicare", -300m), ("facility", "Facility", -20m), ("not-recorded", "Not recorded", 10m)],
            result.Rows.Select(r => (r.Key, r.Name, r.Change)));
        AssertAddsUp(result, 1640m, 700m);
    }

    [Fact]
    public async Task All_three_cuts_of_the_same_scope_agree_on_the_total_and_on_net_cash_by_group_which_is_the_insight_headline()
    {
        Metrics metrics = await NewAsync();
        var byGroup = new NetCashByGroupMetric(metrics.Warehouse, new FakeProviderResolver(null));

        NetCashByGroup groups = await byGroup.GetNetCashByGroupAsync(2026, Oct, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);
        decimal now = groups.Points.Where(p => p.Period == Oct).Sum(p => p.Cash);
        decimal before = groups.Points.Where(p => p.Period == Oct.AddMonths(-1)).Sum(p => p.Cash);

        foreach (CashContribution result in new[] { await metrics.AircraftAsync(), await metrics.DivisionAsync(), await metrics.PayerAsync() })
        {
            Assert.Equal((now, before), (result.NetCash, result.PriorNetCash));
        }
    }

    [Fact]
    public async Task Old_ar_imports_other_providers_and_older_loads_are_not_counted()
    {
        Metrics metrics = await NewAsync();

        CashContribution result = await metrics.AircraftAsync(line: ServiceLineFilter.Both);

        // 99,900 cents of Old AR Import on N123AB, 5,000 of another Provider's N999ZZ and an older load of encounter 1 would each show here.
        Assert.DoesNotContain(result.Rows, r => r.Name == "N999ZZ");
        Assert.Equal(1050m, result.Rows.Single(r => r.Name == "N123AB").NetCash);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        Metrics metrics = await NewAsync();

        CashContribution all = await metrics.PayerAsync(line: ServiceLineFilter.Both);
        CashContribution rotor = await metrics.PayerAsync(division: RotorDivision, line: ServiceLineFilter.Both);
        CashContribution fixedTrips = await metrics.PayerAsync(division: FixedDivision, line: ServiceLineFilter.Both);
        CashContribution tails = await metrics.AircraftAsync(division: FixedDivision);

        Assert.NotEqual(0m, rotor.NetCash);
        Assert.NotEqual(0m, fixedTrips.NetCash);
        // The trips with no division belong to neither named division, so the two named ones are the whole except those (10).
        Assert.Equal(all.NetCash - 10m, rotor.NetCash + fixedTrips.NetCash);
        Assert.Equal(all.PriorNetCash, rotor.PriorNetCash + fixedTrips.PriorNetCash);
        Assert.Equal(["N789EF", "N456CD"], tails.Rows.Select(r => r.Name));
        AssertAddsUp(tails, 600m, 400m);
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("x' OR '1'='1")]
    [InlineData("")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        Metrics metrics = await NewAsync();

        CashContribution aircraft = await metrics.AircraftAsync(division: new ReportingDivision("k", name));
        CashContribution division = await metrics.DivisionAsync(division: new ReportingDivision("k", name));
        CashContribution payer = await metrics.PayerAsync(division: new ReportingDivision("k", name));

        // An empty name is "no division". Anything else matches no trip, so there is nothing to contribute.
        decimal expected = name.Length == 0 ? 1640m : 0m;
        Assert.All(new[] { aircraft, division, payer }, r => Assert.Equal(expected, r.NetCash));
    }

    [Fact]
    public async Task Hostile_tail_text_is_cleaned_cut_and_a_real_tail_called_other_is_not_the_roll_up_row()
    {
        SeedTrip[] hostile =
        [
            new(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "A\tB\r\nC\u0001D"),
            new(2, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: new string('x', 500)),
            new(3, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "   "),
            new(4, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "Other"),
            new(5, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "=HYPERLINK(\"http://evil\")"),
        ];
        Metrics metrics = await NewAsync(hostile, Enumerable.Range(1, 5).Select(i => new SeedCash(i, i, 1, i * 100)));

        CashContribution result = await metrics.AircraftAsync();

        string[] tails = [.. result.Rows.Select(r => r.Name)];
        Assert.Contains("A B C D", tails);
        Assert.Contains(new string('x', 120), tails);
        Assert.Contains("Not recorded", tails);
        Assert.Contains("Other tail", tails);
        Assert.Contains("=HYPERLINK(\"http://evil\")", tails);
        Assert.All(tails, t => Assert.True(t.Length <= 120 && !t.Any(char.IsControl)));
        Assert.DoesNotContain(result.Rows, r => r.IsOther);
        AssertAddsUp(result, 15m, 0m);
    }

    [Fact]
    public async Task The_list_is_capped_at_25_plus_one_other_row_that_keeps_the_total_exact()
    {
        // 30 tails whose changes are 1 to 30 dollars: the 5 smallest (1 to 5, total 15) are rolled into Other.
        SeedTrip[] many = [.. Enumerable.Range(1, 30).Select(i => new SeedTrip(i, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 10, Rotor, Vehicle: $"T{i:D2}"))];
        SeedCash[] cash = [.. Enumerable.Range(1, 30).Select(i => new SeedCash(i, i, 1, i * 100))];
        Metrics metrics = await NewAsync(many, cash);

        CashContribution result = await metrics.AircraftAsync();

        Assert.Equal(26, result.Rows.Count);
        ContributionRow other = result.Rows[^1];
        Assert.Equal((Contribution.OtherName, true, null, 15m), (other.Name, other.IsOther, other.Key, other.Change));
        Assert.Equal("T30", result.Rows[0].Name);
        Assert.Equal("T06", result.Rows[^2].Name);
        AssertAddsUp(result, 465m, 0m);
    }

    [Fact]
    public async Task A_provider_with_no_cash_has_no_rows_and_a_zero_total()
    {
        Metrics metrics = await NewAsync([new SeedTrip(1, "Rotor Wing", null, "A", "Insurance", "2026-10-03", 1, Rotor, Vehicle: "N1")], []);

        CashContribution result = await metrics.AircraftAsync();

        Assert.Empty(result.Rows);
        AssertAddsUp(result, 0m, 0m);
    }

    [Fact]
    public async Task Nothing_that_could_identify_a_patient_is_selected_or_returned()
    {
        Metrics metrics = await NewAsync();
        string[] sql = [CashContributionByAircraftMetric.ByAircraftSql, CashContributionByDivisionMetric.ByDivisionSql, CashContributionByPayerMetric.ByPayerSql];

        string json = JsonSerializer.Serialize(new[] { await metrics.AircraftAsync(), await metrics.DivisionAsync(), await metrics.PayerAsync() });

        // Every seeded trip row carries the PHI marker in its origin address columns, so a query that selected one would show it.
        Assert.DoesNotContain(PhiMarker, json, StringComparison.Ordinal);
        Assert.All(sql, text => Assert.DoesNotContain("origin_", text, StringComparison.Ordinal));
        Assert.All(sql, text => Assert.Contains("@provider_id", text, StringComparison.Ordinal));
    }
}
