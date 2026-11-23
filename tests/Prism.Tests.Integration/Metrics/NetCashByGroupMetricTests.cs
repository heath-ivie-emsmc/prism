using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The net cash by group query against a real PostgreSQL holding tables shaped like the <c>ods</c> ledger and <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class NetCashByGroupMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly ApPeriod Oct = new(2026, 10);

    private async Task<NetCashByGroupMetric> NewMetricAsync()
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
                DROP TABLE IF EXISTS ods.accounting_period, ods.credit, ods.encounter_transaction, ods.encounter_transaction_credit_attribute, reporting.trip_detail_snapshot;
                CREATE TABLE ods.accounting_period (id int PRIMARY KEY, provider_id int NOT NULL, name varchar NOT NULL, is_open boolean NOT NULL DEFAULT false);
                CREATE TABLE ods.credit (id int PRIMARY KEY, credit_type_id int NOT NULL);
                CREATE TABLE ods.encounter_transaction (id bigint PRIMARY KEY, encounter_id bigint NOT NULL, type text NOT NULL, user_source varchar, accounting_period_id int NOT NULL, balance_amount int NOT NULL);
                CREATE TABLE ods.encounter_transaction_credit_attribute (id bigint PRIMARY KEY, encounter_transaction_id bigint NOT NULL, credit_id int NOT NULL);
                CREATE TABLE reporting.trip_detail_snapshot (snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text, reporting_division text);

                -- Provider 2026: Oct 2026 (1) with its adjustment (2), Sep 2026 (3), Oct 2025 (4, twelve months back, in the window), Sep 2025 (5, thirteen back, out).
                -- Provider 777 has an Oct 2026 period (9).
                INSERT INTO ods.accounting_period VALUES (1, 2026, '202610', false), (2, 2026, '-202610', true), (3, 2026, '202609', false), (4, 2026, '202510', false), (5, 2026, '202509', false), (9, 777, '202610', true);
                INSERT INTO ods.credit VALUES (1, 1), (2, 2), (5, 5);

                -- 11 and 12 rotor, 13 fixed, 14 ground ALS, 15 not in the trip table, 16 fixed in an old load only.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 11, 2026, 'Rotor Wing'),
                    ('2026-11-21', 12, 2026, 'Rotor Wing'),
                    ('2026-11-21', 13, 2026, 'Fixed Wing'),
                    ('2026-11-21', 14, 2026, 'ALS'),
                    ('2026-11-21', 21, 777,  'Rotor Wing'),
                    ('2026-10-01', 16, 2026, 'Fixed Wing');

                INSERT INTO ods.encounter_transaction VALUES
                    (1, 11, 'Credit', 'Posting', 1, 100000),        -- $1,000.00 rotor, Oct
                    (2, 11, 'Credit', 'Posting', 2, -5000),         -- $50.00 refund in the adjustment period, same month
                    (3, 12, 'Credit', 'Posting', 1, 20000),         -- $200.00 rotor, Oct
                    (4, 13, 'Credit', 'Posting', 1, 300000),        -- $3,000.00 fixed, Oct
                    (5, 13, 'Credit', 'Posting', 3, 250000),        -- $2,500.00 fixed, Sep
                    (6, 13, 'Credit', 'Posting', 4, 400000),        -- $4,000.00 fixed, Oct 2025
                    (7, 13, 'Credit', 'Posting', 5, 999000),        -- Sep 2025, outside the window
                    (8, 14, 'Credit', 'Posting', 1, 40000),         -- $400.00 ground
                    (9, 15, 'Credit', 'Posting', 1, 7000),          -- no trip row, no group
                    (10, 11, 'Credit', 'Old AR Import', 1, 888800), -- imported, excluded
                    (11, 21, 'Credit', 'Posting', 9, 5550000);      -- another Provider
                INSERT INTO ods.encounter_transaction_credit_attribute VALUES
                    (1, 1, 2), (2, 2, 5), (3, 3, 2), (4, 4, 2), (5, 5, 2), (6, 6, 2), (7, 7, 2), (8, 8, 2), (9, 9, 2), (10, 10, 2), (11, 11, 2);
                

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
        return new NetCashByGroupMetric(warehouse, new FakeProviderResolver(null));
    }

    [Fact]
    public async Task Air_cash_is_split_by_group_and_posting_month_across_the_twelve_month_window()
    {
        NetCashByGroupMetric metric = await NewMetricAsync();

        NetCashByGroup result = await metric.GetNetCashByGroupAsync(2026, Oct, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        Assert.Equal("net_cash_by_group", result.Metric.Key);
        Assert.Equal(
            [
                ("fixed-wing", new ApPeriod(2025, 10), 4000m, 1),
                ("fixed-wing", new ApPeriod(2026, 9), 2500m, 1),
                ("fixed-wing", Oct, 3000m, 1),
                ("rotor-wing", Oct, 1150m, 2),
            ],
            result.Points.Select(p => (p.GroupKey, p.Period, p.Cash, p.Transports)));
        Assert.All(result.Points, p => Assert.True(p.IsAir));
    }

    [Fact]
    public async Task The_adjustment_period_joins_its_month_and_names_match_the_breakdown()
    {
        NetCashByGroupMetric metric = await NewMetricAsync();

        NetCashByGroup result = await metric.GetNetCashByGroupAsync(2026, Oct, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);

        // Rotor, Oct: $1,000 less the $50 refund posted in the adjustment period, plus $200.
        NetCashByGroupPoint rotor = result.Points.Single(p => p.GroupKey == "rotor-wing");
        Assert.Equal(("Rotor wing", 1150m), (rotor.GroupName, rotor.Cash));
    }

    [Fact]
    public async Task Ground_is_a_separate_service_line_and_cash_with_no_trip_row_has_no_group()
    {
        NetCashByGroupMetric metric = await NewMetricAsync();

        NetCashByGroup ground = await metric.GetNetCashByGroupAsync(2026, Oct, ServiceLineFilter.Ground, null, TestContext.Current.CancellationToken);
        NetCashByGroup both = await metric.GetNetCashByGroupAsync(2026, Oct, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        NetCashByGroupPoint only = Assert.Single(ground.Points);
        Assert.Equal(("als", 400m, false), (only.GroupKey, only.Cash, only.IsAir));
        // Air 10,650 + ground 400. The $70 with no trip row, the import, the Sep 2025 cash and the other Provider's $55,500 never appear.
        Assert.Equal(1150m + 3000m + 2500m + 4000m + 400m, both.Points.Sum(p => p.Cash));
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(NetCashByGroupMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetNetCashByGroupAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, division, TestContext.Current.CancellationToken);
        return mix.Points.Sum(p => p.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        NetCashByGroupMetric metric = await NewMetricAsync();

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
        NetCashByGroupMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
