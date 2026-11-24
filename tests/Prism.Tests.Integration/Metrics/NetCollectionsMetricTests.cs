using Prism.Testing.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The net cash queries against a real PostgreSQL holding a table shaped like <c>reporting.kpi_net_collections_ap__prov</c>.</summary>
public sealed class NetCollectionsMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<NetCollectionsMetric> NewMetricAsync(IReadOnlyDictionary<int, int>? providerMap = null)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.kpi_net_collections_ap__prov;
                CREATE TABLE reporting.kpi_net_collections_ap__prov (
                    is_current boolean NOT NULL, snapshot_date date NOT NULL, provider_id int NOT NULL,
                    ap_year_month char(7) NOT NULL, net_collections numeric);
                -- An old load: different numbers, must be ignored.
                INSERT INTO reporting.kpi_net_collections_ap__prov VALUES
                    (false, '2026-03-31', 2026, '202501', 1), (false, '2026-03-31', 2026, '202502', 1);
                -- The current load. Months 202503 and 202508 are missing. 202506 has two rows (two pods) that add up.
                INSERT INTO reporting.kpi_net_collections_ap__prov VALUES
                    (true, '2026-11-05', 2026, '202501', 100), (true, '2026-11-05', 2026, '202502', 200),
                    (true, '2026-11-05', 2026, '202504', 400), (true, '2026-11-05', 2026, '202505', 500),
                    (true, '2026-11-05', 2026, '202506', 300), (true, '2026-11-05', 2026, '202506', 350),
                    (true, '2026-11-05', 2026, '202507', 700), (true, '2026-11-05', 2026, '202509', 900),
                    (true, '2026-11-05', 2026, '202510', NULL);
                -- Another Provider, never returned for 2026.
                INSERT INTO reporting.kpi_net_collections_ap__prov VALUES
                    (true, '2026-11-05', 777, '202506', 99999), (true, '2026-11-05', 777, '202511', 88888);
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
        return new NetCollectionsMetric(warehouse, new FakeProviderResolver(providerMap));
    }

    [Fact]
    public async Task Periods_are_the_months_of_the_current_load_newest_first()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        IReadOnlyList<ApPeriod> periods = await metric.GetPeriodsAsync(2026, TestContext.Current.CancellationToken);

        // 202510 exists but has no amount yet, so it is not a period with data.
        Assert.Equal(["202509", "202507", "202506", "202505", "202504", "202502", "202501"], periods.Select(p => p.ToString()));
    }

    [Fact]
    public async Task The_series_covers_the_requested_months_ends_at_the_end_period_and_sums_rows_in_a_month()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), 4, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // 202504..202507. Two rows in 202506 add up. The old load's rows are never read.
        Assert.Equal([("202504", 400m), ("202505", 500m), ("202506", 650m), ("202507", 700m)], series.Points.Select(p => (p.Period.ToString(), p.Value)));
        Assert.Equal(new DateOnly(2026, 11, 5), series.SnapshotDate);
        Assert.Equal("net_collections", series.Metric.Key);
    }

    [Fact]
    public async Task Months_with_no_data_or_no_amount_are_left_out_never_shown_as_zero()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 10), 3, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Equal([("202509", 900m)], series.Points.Select(p => (p.Period.ToString(), p.Value)));
    }

    [Fact]
    public async Task Another_providers_rows_are_never_returned()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 11), 24, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(series.Points, p => p.Value is 99999m or 88888m);
        Assert.Empty((await metric.GetSeriesAsync(31337, new ApPeriod(2025, 11), 24, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken)).Points);
        Assert.Empty(await metric.GetPeriodsAsync(31337, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_development_provider_replaces_the_requested_provider_only_when_set()
    {
        NetCollectionsMetric standIn = await NewMetricAsync(new Dictionary<int, int> { [526] = 2026 });

        NetCollectionsSeries series = await standIn.GetSeriesAsync(526, new ApPeriod(2025, 7), 1, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Equal(700m, Assert.Single(series.Points).Value);
    }

    [Fact]
    public async Task A_window_that_is_too_long_or_empty_is_refused()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), 0, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), INetCollectionsMetric.MaxMonths + 1, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken));
    }

    // ---- One Reporting Division: the ledger joined to the trip table, against the KPI headline ----

    // Trips: 1 and 2 Rotor, 3 and 4 Fixed (3 under the legacy "- Old AR" name), 5 ground in Rotor, 8 with no division. Encounter 99 has cash and no trip row.
    private static readonly SeedTrip[] DivisionTrips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, FleetSeed.Rotor),
        new(2, "Rotor Wing", null, "MEDICARE", "Medicare", "2026-09-04", 50, FleetSeed.Rotor),
        new(3, "Fixed Wing", null, "MEDICAID", "Medicaid", "2026-10-05", 30, FleetSeed.FixedLegacy),
        new(4, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-06", 200, FleetSeed.Fixed),
        new(5, "ALS", null, "SELF", "Patient", "2026-10-08", 10, FleetSeed.Rotor),
        new(8, "Rotor Wing", null, "ODD", "Something Else", "2026-10-09", 5, "<none>"),
        new(11, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Provider: 777),
    ];

    // Oct (period 1, adjustment 3): 1000 + 50 + 500 + 100 + 200 + 10 + 70 (no trip row) = 1930. Sep (2): 300 + 400 = 700. The import and another Provider never count.
    private static readonly SeedCash[] DivisionCash =
    [
        new(1, 1, 1, 100000),
        new(2, 2, 2, 30000),
        new(3, 3, 1, 50000),
        new(4, 4, 2, 40000),
        new(5, 4, 1, 10000),
        new(6, 1, 3, 5000),
        new(7, 5, 1, 20000),
        new(8, 8, 1, 1000),
        new(9, 99, 1, 7000),
        new(10, 2, 1, 99900, "Old AR Import"),
        new(11, 11, 9, 500000),
    ];

    private async Task<NetCollectionsMetric> NewDivisionMetricAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await FleetSeed.CreateAsync(postgres, DivisionTrips, DivisionCash, ct);
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var kpi = new NpgsqlCommand(
                """
                DROP TABLE IF EXISTS reporting.kpi_net_collections_ap__prov;
                CREATE TABLE reporting.kpi_net_collections_ap__prov (
                    is_current boolean NOT NULL, snapshot_date date NOT NULL, provider_id int NOT NULL,
                    ap_year_month char(7) NOT NULL, net_collections numeric);
                -- The headline, from an older load date than the trip table: the same cash rule, so it equals the ledger.
                INSERT INTO reporting.kpi_net_collections_ap__prov VALUES
                    (true, '2026-11-05', 2026, '202609', 700), (true, '2026-11-05', 2026, '202610', 1930),
                    (true, '2026-11-05', 777, '202610', 5000);
                """,
                admin);
            await kpi.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        return new NetCollectionsMetric(warehouse, new FakeProviderResolver(null));
    }

    private static Task<NetCollectionsSeries> DivisionSeriesAsync(NetCollectionsMetric metric, ReportingDivision? division, ServiceLineFilter line = ServiceLineFilter.Both) =>
        metric.GetSeriesAsync(2026, new ApPeriod(2026, 10), 2, line, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_add_up_to_the_headline_less_the_cash_that_has_no_division()
    {
        NetCollectionsMetric metric = await NewDivisionMetricAsync();

        NetCollectionsSeries headline = await DivisionSeriesAsync(metric, null);
        NetCollectionsSeries rotor = await DivisionSeriesAsync(metric, FleetSeed.RotorDivision);
        // The Fixed trips are filed under a legacy "- Old AR" name, which counts under the current division.
        NetCollectionsSeries fixedTrips = await DivisionSeriesAsync(metric, FleetSeed.FixedDivision);

        Assert.Equal([(new ApPeriod(2026, 9), 700m), (new ApPeriod(2026, 10), 1930m)], headline.Points.Select(p => (p.Period, p.Value)));
        Assert.Equal([(new ApPeriod(2026, 9), 300m), (new ApPeriod(2026, 10), 1250m)], rotor.Points.Select(p => (p.Period, p.Value)));
        Assert.Equal([(new ApPeriod(2026, 9), 400m), (new ApPeriod(2026, 10), 600m)], fixedTrips.Points.Select(p => (p.Period, p.Value)));
        // The known gap, stated: $10 on a trip with no division and $70 on an encounter with no trip row belong to no division. Nothing else is missing.
        const decimal NoDivisionOct = 10m + 70m;
        Assert.Equal(headline.Points[1].Value - NoDivisionOct, rotor.Points[1].Value + fixedTrips.Points[1].Value);
        Assert.Equal(headline.Points[0].Value, rotor.Points[0].Value + fixedTrips.Points[0].Value);
    }

    [Fact]
    public async Task A_division_and_a_service_line_together_keep_only_that_service_line_of_that_division()
    {
        NetCollectionsMetric metric = await NewDivisionMetricAsync();

        NetCollectionsSeries air = await DivisionSeriesAsync(metric, FleetSeed.RotorDivision, ServiceLineFilter.Air);
        NetCollectionsSeries ground = await DivisionSeriesAsync(metric, FleetSeed.RotorDivision, ServiceLineFilter.Ground);

        // Rotor, Oct: air 1000 + 50, ground 200. No ground in Sep, no air in the Fixed division's ground.
        Assert.Equal([(new ApPeriod(2026, 9), 300m), (new ApPeriod(2026, 10), 1050m)], air.Points.Select(p => (p.Period, p.Value)));
        Assert.Equal([(new ApPeriod(2026, 10), 200m)], ground.Points.Select(p => (p.Period, p.Value)));
    }

    [Fact]
    public async Task With_a_division_the_data_through_is_the_trip_tables_load_and_without_one_it_is_the_kpi_tables()
    {
        NetCollectionsMetric metric = await NewDivisionMetricAsync();

        NetCollectionsSeries headline = await DivisionSeriesAsync(metric, null);
        NetCollectionsSeries rotor = await DivisionSeriesAsync(metric, FleetSeed.RotorDivision);

        Assert.Equal(new DateOnly(2026, 11, 5), headline.SnapshotDate);
        Assert.Equal(new DateOnly(2026, 11, 21), rotor.SnapshotDate);
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("<none>")]
    [InlineData("x' OR '1'='1")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        NetCollectionsMetric metric = await NewDivisionMetricAsync();

        NetCollectionsSeries series = await DivisionSeriesAsync(metric, new ReportingDivision("k", name));

        // Another Provider's division, a legacy spelling, the "no division" marker and an injection string are all just names that match no trip of this Provider.
        Assert.Empty(series.Points);
    }
}
