using Prism.Testing.Common;
using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The breakdown query against a real PostgreSQL holding tables shaped like the <c>ods</c> ledger and <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class NetCashBreakdownMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<NetCashBreakdownMetric> NewMetricAsync(IReadOnlyDictionary<int, int>? providerMap = null)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS ods;
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS ods.accounting_period, ods.credit, ods.encounter_transaction, ods.encounter_transaction_credit_attribute, reporting.trip_detail_snapshot;
                CREATE TABLE ods.accounting_period (id int PRIMARY KEY, provider_id int NOT NULL, name varchar NOT NULL, is_open boolean NOT NULL DEFAULT false);
                CREATE TABLE ods.credit (id int PRIMARY KEY, credit_type_id int NOT NULL);
                CREATE TABLE ods.encounter_transaction (id bigint PRIMARY KEY, encounter_id bigint NOT NULL, type text NOT NULL, user_source varchar, accounting_period_id int NOT NULL, balance_amount int NOT NULL);
                CREATE TABLE ods.encounter_transaction_credit_attribute (id bigint PRIMARY KEY, encounter_transaction_id bigint NOT NULL, credit_id int NOT NULL);
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, provider_name text, reporting_division text,
                    vehicle text, level_of_service text, response_type text, loaded_miles numeric);

                -- Provider 2026: Oct (periods 1 and its adjustment 2) and Sep (3). Provider 777 has an Oct period of its own (9).
                INSERT INTO ods.accounting_period VALUES (1, 2026, '202610', false), (2, 2026, '-202610', true), (3, 2026, '202609', false), (9, 777, '202610', true);
                -- credit types: 2 and 5 are payments and refunds, 1 is an adjustment.
                INSERT INTO ods.credit VALUES (1, 1), (2, 2), (5, 5);

                -- Encounters: 11 rotor (Rotor base, tail N1), 12 rotor at a legacy base, 13 fixed (N2), 14 ground ALS (unit 12), 15 not in the trip table,
                -- 16 rotor in Sep only.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 11, 2026, 'Mercy Air', 'Mercy Air - Rotor', 'N1', 'Rotor Wing', 'Scene (Air)', 10),
                    ('2026-11-21', 12, 2026, 'Mercy Air', 'Mercy Air - Rotor - Old AR Logis', 'N1', 'Rotor Wing', 'Interfacility (Air)', 20),
                    ('2026-11-21', 13, 2026, 'Mercy Air', 'Mercy Air - Fixed', 'N2', 'Fixed Wing', 'Interfacility (Air)', 100),
                    ('2026-11-21', 14, 2026, 'Mercy Air', '<none>', '12', 'ALS', 'Emergency', 5),
                    ('2026-11-21', 16, 2026, 'Mercy Air', 'Mercy Air - Rotor', 'N1', 'Rotor Wing', 'Scene (Air)', 8),
                    ('2026-11-21', 21, 777, 'Other', 'Other - Rotor', 'X9', 'Rotor Wing', 'Scene (Air)', 1),
                    ('2026-10-01', 11, 2026, 'Mercy Air', 'Mercy Air - OLD LOAD', 'OLD', 'Rotor Wing', 'Scene (Air)', 99);

                INSERT INTO ods.encounter_transaction VALUES
                    (1, 11, 'Credit', 'Posting', 1, 100000),   -- $1,000.00 payment, Oct
                    (2, 11, 'Credit', 'Posting', 2, -5000),    -- $50.00 refund, Oct adjustment period: same month
                    (3, 12, 'Credit', NULL, 1, 20000),         -- $200.00, Oct, legacy base
                    (4, 13, 'Credit', 'Posting', 1, 300000),   -- $3,000.00, Oct
                    (5, 14, 'Credit', 'Posting', 1, 40000),    -- $400.00, Oct
                    (6, 15, 'Credit', 'Posting', 1, 7000),     -- $70.00, Oct, no trip row
                    (7, 11, 'Credit', 'Old AR Import', 1, 999900), -- imported, excluded
                    (8, 11, 'Credit', 'Posting', 1, 888800),   -- only an adjustment credit, excluded
                    (9, 11, 'Debit', 'Posting', 1, 777700),    -- not a credit, excluded
                    (10, 16, 'Credit', 'Posting', 3, 60000),   -- $600.00, Sep
                    (11, 13, 'Credit', 'Posting', 3, 25000),   -- $250.00, Sep
                    (12, 21, 'Credit', 'Posting', 9, 5550000), -- another Provider's period
                    (13, 11, 'Credit', 'Posting', 1, 10000);   -- two credit attributes on one transaction: counted once ($100.00)
                INSERT INTO ods.encounter_transaction_credit_attribute VALUES
                    (1, 1, 2), (2, 2, 5), (3, 3, 2), (4, 4, 2), (5, 5, 2), (6, 6, 2), (7, 7, 2), (8, 8, 1), (9, 9, 2),
                    (10, 10, 2), (11, 11, 2), (12, 12, 2), (13, 13, 2), (14, 13, 5);
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        return new NetCashBreakdownMetric(warehouse, new FakeProviderResolver(providerMap));
    }

    [Fact]
    public async Task Net_cash_is_summed_by_posting_month_with_adjustment_periods_folded_in_and_the_old_import_left_out()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown result = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // Oct: 1000 - 50 + 100 (enc 11) + 200 + 3000 + 400 + 70 = 4720. Sep: 600 + 250 = 850.
        Assert.Equal(4720m, result.Groups.Sum(g => g.Current.Cash));
        Assert.Equal(850m, result.Groups.Sum(g => g.Prior.Cash));
        Assert.Equal(new ApPeriod(2026, 9), result.PriorPeriod);
        Assert.Equal("net_cash_breakdown", result.Metric.Key);
    }

    [Fact]
    public async Task The_net_cash_series_for_air_or_ground_alone_adds_up_to_the_whole_and_matches_the_breakdown()
    {
        NetCashBreakdownMetric breakdown = await NewMetricAsync();
        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        var series = new NetCollectionsMetric(warehouse, new FakeProviderResolver(null));
        CancellationToken ct = TestContext.Current.CancellationToken;

        NetCollectionsSeries air = await series.GetSeriesAsync(2026, new ApPeriod(2026, 10), 2, ServiceLineFilter.Air, ct);
        NetCollectionsSeries ground = await series.GetSeriesAsync(2026, new ApPeriod(2026, 10), 2, ServiceLineFilter.Ground, ct);

        // Air: Oct 1000 - 50 + 100 + 200 + 3000 = 4250, Sep 600 + 250 = 850. Ground: Oct 400 + 70 (no trip row) = 470, nothing in Sep.
        Assert.Equal([(new ApPeriod(2026, 9), 850m), (new ApPeriod(2026, 10), 4250m)], air.Points.Select(p => (p.Period, p.Value)));
        Assert.Equal([(new ApPeriod(2026, 10), 470m)], ground.Points.Select(p => (p.Period, p.Value)));
        Assert.Equal(ServiceLineFilter.Air, air.ServiceLine);
        NetCashBreakdown air2 = await breakdown.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, ct);
        Assert.Equal(air.Points[^1].Value, air2.Groups.Sum(g => g.Current.Cash));
        // Another Provider's 5,550 never leaks in.
        Assert.DoesNotContain(air.Points.Concat(ground.Points), p => p.Value > 5000m);
    }

    [Fact]
    public async Task Air_is_grouped_by_aircraft_type_with_transports_counted_by_encounter_and_the_mix()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown result = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        Assert.Equal(["fixed-wing", "rotor-wing"], result.Groups.Select(g => g.Key));
        NetCashBreakdownGroup rotor = result.Groups.Single(g => g.Key == "rotor-wing");
        Assert.Equal("Rotor wing", rotor.Name);
        Assert.True(rotor.IsAir);
        // Encounters 11 and 12 had cash in Oct: 1050 + 200 = 1250, two transports, 30 miles, one scene and one interfacility.
        Assert.Equal(new BreakdownFigures(1250m, 2, 30m, 1, 1), rotor.Current);
        // Sep: encounter 16 only.
        Assert.Equal(new BreakdownFigures(600m, 1, 8m, 1, 0), rotor.Prior);
        Assert.Equal(new BreakdownFigures(3000m, 1, 100m, 0, 1), result.Groups.Single(g => g.Key == "fixed-wing").Current);
    }

    [Fact]
    public async Task Rows_are_per_vehicle_and_a_legacy_base_is_counted_under_the_current_one()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown result = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        // N1 at "Mercy Air - Rotor": encounter 11 and encounter 12 (its "- Old AR Logis" division collapses to "Mercy Air - Rotor"). The Provider's name stays: it is the reporting division's name.
        NetCashBreakdownRow n1 = Assert.Single(result.Rows, r => r.Vehicle == "N1");
        Assert.Equal("Mercy Air - Rotor", n1.Base);
        Assert.Equal(1250m, n1.Current.Cash);
        Assert.Equal(600m, n1.Prior.Cash);
        Assert.Equal(("N2", "Mercy Air - Fixed"), (result.Rows[0].Vehicle, result.Rows[0].Base));
        Assert.DoesNotContain(result.Rows, r => r.Vehicle == "OLD");
    }

    [Fact]
    public async Task Ground_is_grouped_by_level_of_service_and_encounters_without_a_trip_row_are_not_recorded()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown result = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Ground, null, TestContext.Current.CancellationToken);

        Assert.Equal(["als", "not-recorded"], result.Groups.Select(g => g.Key));
        Assert.Equal(new BreakdownFigures(400m, 1, 5m, 1, 0), result.Groups[0].Current);
        Assert.Equal("Advanced life support", result.Groups[0].Name);
        Assert.False(result.Groups[0].IsAir);
        NetCashBreakdownRow als = result.Rows[0];
        Assert.Equal(("12", null), (als.Vehicle, als.Base));
        Assert.Equal("Not recorded", result.Rows[1].Vehicle);
    }

    [Fact]
    public async Task Another_providers_cash_is_never_returned()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown other = await metric.GetBreakdownAsync(31337, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);
        NetCashBreakdown mine = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Empty(other.Groups);
        Assert.DoesNotContain(mine.Rows, r => r.Vehicle == "X9");
        Assert.True(mine.Groups.Sum(g => g.Current.Cash) < 50000m);
    }

    [Fact]
    public async Task A_period_is_open_while_it_or_its_adjustment_period_is_open_and_closed_otherwise()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown october = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);
        NetCashBreakdown september = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 9), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);
        NetCashBreakdown unknown = await metric.GetBreakdownAsync(2026, new ApPeriod(2030, 1), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // October's adjustment period is open. Another Provider's open October does not count for this one.
        Assert.True(october.PeriodOpen);
        Assert.False(september.PeriodOpen);
        Assert.False(unknown.PeriodOpen);
    }

    [Fact]
    public async Task A_period_with_no_cash_gives_no_groups()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        NetCashBreakdown result = await metric.GetBreakdownAsync(2026, new ApPeriod(2030, 1), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Groups);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task The_portal_id_is_translated_to_the_warehouse_provider_before_the_query()
    {
        NetCashBreakdownMetric standIn = await NewMetricAsync(new Dictionary<int, int> { [31337] = 2026 });

        NetCashBreakdown result = await standIn.GetBreakdownAsync(31337, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        Assert.Equal(["fixed-wing", "rotor-wing"], result.Groups.Select(g => g.Key));
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Air - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Air - Fixed");

    private static async Task<int> TotalAsync(NetCashBreakdownMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetBreakdownAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, division, TestContext.Current.CancellationToken);
        return mix.Groups.Sum(g => g.Current.Transports + g.Prior.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        int all = await TotalAsync(metric, null);
        int rotor = await TotalAsync(metric, Rotor);
        // Encounter 12 is filed under a legacy "- Old AR" name and counts under Rotor. The ground trip has no division and the cash with no trip row has none either.
        int fixedTrips = await TotalAsync(metric, Fixed);

        Assert.True(rotor > 0);
        Assert.True(fixedTrips > 0);
        Assert.True(rotor < all);
        Assert.True(rotor + fixedTrips <= all);
    }

    [Theory]
    [InlineData("Other - Rotor")]
    [InlineData("Mercy Air - Rotor - Old AR Logis")]
    [InlineData("x' OR '1'='1")]
    [InlineData("")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        NetCashBreakdownMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
