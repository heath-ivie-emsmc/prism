using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The transport volume query against a real PostgreSQL holding tables shaped like <c>reporting.trip_detail_snapshot</c> and <c>ods.accounting_period</c>.</summary>
public sealed class TransportVolumeMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<TransportVolumeMetric> NewMetricAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS ods;
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS ods.accounting_period, reporting.trip_detail_snapshot;
                CREATE TABLE ods.accounting_period (id int PRIMARY KEY, provider_id int NOT NULL, name varchar NOT NULL, is_open boolean NOT NULL DEFAULT false);
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text, loaded_miles numeric, dos date, reporting_division text);

                -- Oct is open, Sep is closed. Provider 777 has an open Oct of its own and a closed one for 2026.
                INSERT INTO ods.accounting_period VALUES (1, 2026, '202610', false), (2, 2026, '-202610', true), (3, 2026, '202609', false), (9, 777, '202610', false);

                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 1,  2026, 'Rotor Wing', 100, '2026-10-02'),
                    ('2026-11-21', 2,  2026, 'Rotor Wing', 50.5, '2026-10-20'),
                    ('2026-11-21', 3,  2026, 'Fixed Wing', 300, '2026-10-21'),
                    ('2026-11-21', 4,  2026, 'ALS', 12, '2026-10-22'),
                    ('2026-11-21', 5,  2026, 'Rotor Wing', NULL, '2026-10-23'),
                    ('2026-11-21', 6,  2026, 'Rotor Wing', 80, '2026-09-30'),
                    ('2026-11-21', 7,  2026, 'Fixed Wing', 200, '2026-09-01'),
                    ('2026-11-21', 8,  2026, 'Rotor Wing', 70, '2026-07-15'),
                    ('2026-11-21', 9,  2026, 'Rotor Wing', 999, '2026-11-01'),
                    ('2026-11-21', 10, 2026, 'Rotor Wing', 999, '2025-12-31'),
                    ('2026-11-21', 21, 777,  'Rotor Wing', 5000, '2026-10-05'),
                    ('2026-10-01', 1,  2026, 'Rotor Wing', 7777, '2026-10-02');
                

                -- Reporting divisions: even encounters are the Rotor division, odd ones the Fixed division (filed under a legacy "- Old AR" name that counts as the current one).
                -- Provider 777 has a division of its own that Provider 2026 must never see.
                UPDATE reporting.trip_detail_snapshot SET reporting_division = CASE WHEN provider_id = 777 THEN 'Other Provider - Base' WHEN encounter_id % 2 = 0 THEN 'Mercy Flights - Rotor' ELSE 'Mercy Flights - Fixed - Old AR 2019' END;
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
        return new TransportVolumeMetric(warehouse, new FakeProviderResolver(null));
    }

    [Fact]
    public async Task Transports_and_loaded_miles_are_summed_by_month_of_service_for_the_months_asked_for()
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        TransportVolume volume = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 4, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        Assert.Equal("transport_volume", volume.Metric.Key);
        // Jul: 1. Aug: none, so left out. Sep: 2. Oct: rotor 1, 2 and 5 plus fixed 3. A null mileage counts as no miles.
        Assert.Equal(
            [(new ApPeriod(2026, 7), 1, 70m), (new ApPeriod(2026, 9), 2, 280m), (new ApPeriod(2026, 10), 4, 450.5m)],
            volume.Points.Select(p => (p.Period, p.Transports, p.LoadedMiles)));
    }

    [Fact]
    public async Task The_last_month_is_split_by_aircraft_type_or_level_of_service_largest_first()
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        TransportVolume volume = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 6, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Equal([("rotor-wing", 3), ("als", 1), ("fixed-wing", 1)], volume.Groups.Select(g => (g.Key, g.Transports)));
        Assert.Equal("Rotor wing", volume.Groups[0].Name);
        Assert.True(volume.Groups[0].IsAir);
        Assert.False(volume.Groups[1].IsAir);
    }

    [Theory]
    [InlineData(ServiceLineFilter.Air, 4)]
    [InlineData(ServiceLineFilter.Ground, 1)]
    [InlineData(ServiceLineFilter.Both, 5)]
    public async Task The_service_line_decides_which_levels_of_service_count(ServiceLineFilter line, int octoberTransports)
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        TransportVolume volume = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 1, line, null, TestContext.Current.CancellationToken);

        Assert.Equal(octoberTransports, volume.Points.Single().Transports);
    }

    [Fact]
    public async Task Another_Providers_transports_older_loads_and_months_outside_the_range_are_not_counted()
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        TransportVolume volume = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 3, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        // Provider 777's 5000 miles, the old load's 7777, the November trip and December 2025 would all show up in these sums if they leaked in.
        Assert.Equal(730.5m, volume.Points.Sum(p => p.LoadedMiles));
        Assert.Equal(6, volume.Points.Sum(p => p.Transports));
    }

    [Fact]
    public async Task The_period_is_open_while_its_accounting_period_or_its_adjustment_period_is_open()
    {
        TransportVolumeMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        TransportVolume october = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 1, ServiceLineFilter.Both, null, ct);
        TransportVolume september = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 9), 1, ServiceLineFilter.Both, null, ct);

        Assert.True(october.PeriodOpen);
        Assert.False(september.PeriodOpen);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 24)]
    public async Task The_number_of_months_is_kept_within_bounds_before_it_reaches_a_query(int asked, int used)
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        TransportVolume volume = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), asked, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Equal(used, volume.Months);
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(TransportVolumeMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetVolumeAsync(2026, new ApPeriod(2026, 10), 4, ServiceLineFilter.Both, division, TestContext.Current.CancellationToken);
        return mix.Points.Sum(p => p.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        int all = await TotalAsync(metric, null);
        int rotor = await TotalAsync(metric, Rotor);
        // The Fixed trips are filed under a legacy "- Old AR" name, which counts under the current division.
        int fixedTrips = await TotalAsync(metric, Fixed);

        Assert.True(rotor > 0);
        Assert.True(fixedTrips > 0);
        Assert.True(rotor < all);
        Assert.Equal(all, rotor + fixedTrips);
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("x' OR '1'='1")]
    [InlineData("")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        TransportVolumeMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
