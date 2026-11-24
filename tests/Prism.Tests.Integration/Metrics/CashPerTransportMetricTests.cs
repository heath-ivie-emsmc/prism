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

/// <summary>The matured cash per transport query against a real PostgreSQL holding tables shaped like the <c>ods</c> ledger and <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class CashPerTransportMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly ApPeriod Oct = new(2026, 10);

    private async Task<CashPerTransportMetric> NewMetricAsync()
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
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text,
                    prim_pay text, prim_pay_category text, dos date, reporting_division text);

                -- Provider 2026, asked about Oct 2026: the cohorts are flown May 2025 to Apr 2026. Periods: Nov 2025 (1), Apr 2026 (2) with its adjustment (4), Oct 2026 (3), Nov 2026 (5, after the chosen period).
                -- Provider 777 has an Apr 2026 period (9).
                INSERT INTO ods.accounting_period VALUES (1, 2026, '202511', false), (2, 2026, '202604', false), (3, 2026, '202610', true), (4, 2026, '-202604', false), (5, 2026, '202611', true), (9, 777, '202604', false);
                INSERT INTO ods.credit VALUES (1, 1), (2, 2), (5, 5);

                -- 11 rotor commercial, paid. 12 rotor commercial, unpaid. 13 rotor Medicaid, paid and partly refunded. 14 fixed Medicare, paid in the chosen period.
                -- 15 too young (June 2026). 16 too old (April 2025). 17 ground commercial. 18 rotor with no payer. 19 rotor commercial paid only after the chosen period.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 11, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2025-09-10'),
                    ('2026-11-21', 12, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-01-15'),
                    ('2026-11-21', 13, 2026, 'Rotor Wing', 'OHP', 'Medicaid MCO', '2026-02-01'),
                    ('2026-11-21', 14, 2026, 'Fixed Wing', 'MEDICARE', 'Medicare', '2025-12-05'),
                    ('2026-11-21', 15, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-06-01'),
                    ('2026-11-21', 16, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2025-04-30'),
                    ('2026-11-21', 17, 2026, 'ALS', 'AETNA', 'Insurance', '2026-02-02'),
                    ('2026-11-21', 18, 2026, 'Rotor Wing', NULL, NULL, '2026-03-01'),
                    ('2026-11-21', 19, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-03-03'),
                    ('2026-11-21', 21, 777,  'Rotor Wing', 'MEDICARE', 'Medicare', '2026-02-01'),
                    ('2026-10-01', 22, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-02-01');

                INSERT INTO ods.encounter_transaction VALUES
                    (1, 11, 'Credit', 'Posting', 1, 200000),        -- $2,000.00
                    (2, 13, 'Credit', 'Posting', 2, 30000),         -- $300.00
                    (3, 13, 'Credit', 'Posting', 4, -5000),         -- $50.00 refund in the adjustment period
                    (4, 14, 'Credit', 'Posting', 3, 80000),         -- $800.00, posted in the chosen period
                    (5, 17, 'Credit', 'Posting', 2, 10000),         -- $100.00
                    (6, 19, 'Credit', 'Posting', 5, 40000),         -- after the chosen period, left out
                    (7, 11, 'Credit', 'Old AR Import', 1, 99900),   -- imported, excluded
                    (8, 21, 'Credit', 'Posting', 9, 500000);        -- another Provider
                INSERT INTO ods.encounter_transaction_credit_attribute VALUES
                    (1, 1, 2), (2, 2, 2), (3, 3, 5), (4, 4, 2), (5, 5, 2), (6, 6, 2), (7, 7, 2), (8, 8, 2);
                

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
        return new CashPerTransportMetric(warehouse, new FakeProviderResolver(null));
    }

    [Fact]
    public async Task Air_transports_and_cash_are_counted_by_payer_for_the_matured_cohorts()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport result = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("cash_per_transport_by_payer", result.Metric.Key);
        Assert.Equal((new ApPeriod(2025, 5), new ApPeriod(2026, 4)), (result.CohortFrom, result.CohortThrough));
        // Commercial: 11 ($2,000), 12 (unpaid), 19 (paid only after the period). Too young (15), too old (16), the import and another Provider's cash are out.
        Assert.Equal((3, 2000m), Pair(result, "commercial"));
        Assert.Equal((1, 800m), Pair(result, "medicare"));
        Assert.Equal((1, 250m), Pair(result, "medicaid"));
        Assert.Equal((1, 0m), Pair(result, "not-recorded"));
        Assert.Equal(0, result.Payers.Single(p => p.Key == "va").Transports);
    }

    [Fact]
    public async Task Cash_per_transport_includes_unpaid_transports_and_is_null_when_there_are_none()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport result = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(2000m / 3, result.Payers.Single(p => p.Key == "commercial").CashPerTransport);
        Assert.Null(result.Payers.Single(p => p.Key == "va").CashPerTransport);
    }

    [Fact]
    public async Task Every_payer_group_is_listed_in_a_fixed_order_and_not_recorded_only_when_it_has_transports()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport air = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);
        CashPerTransport ground = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Ground, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(["commercial", "medicare", "medicaid", "other-gov", "va", "facility", "patient", "not-recorded"], air.Payers.Select(p => p.Key));
        Assert.DoesNotContain(ground.Payers, p => p.Key == "not-recorded");
    }

    [Fact]
    public async Task A_group_key_narrows_it_to_one_aircraft_type()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport result = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, "fixed-wing", null, TestContext.Current.CancellationToken);

        Assert.Equal("fixed-wing", result.GroupKey);
        Assert.Equal((1, 800m), Pair(result, "medicare"));
        Assert.Equal(1, result.Payers.Sum(p => p.Transports));
    }

    [Fact]
    public async Task The_overall_figure_covers_every_payer_and_ignores_the_aircraft_type_filter()
    {
        CashPerTransportMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        CashPerTransport all = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, null, null, ct);
        CashPerTransport fixedWing = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, "fixed-wing", null, ct);

        // Air: commercial 3 ($2,000), Medicare 1 ($800), Medicaid 1 ($250), not recorded 1 ($0).
        Assert.Equal(new OverallCashPerTransport(6, 3050m), all.Overall);
        Assert.Equal(all.Overall, fixedWing.Overall);
        Assert.Equal(800m, fixedWing.Payers.Single(p => p.Key == "medicare").CashPerTransport);
        Assert.Equal(800m - (3050m / 6), fixedWing.Payers.Single(p => p.Key == "medicare").DifferenceFrom(fixedWing.Overall?.CashPerTransport));
    }

    [Fact]
    public async Task The_overall_figure_follows_the_service_line_and_division_filters_and_is_empty_when_nothing_matches()
    {
        CashPerTransportMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        CashPerTransport ground = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Ground, null, null, ct);
        CashPerTransport nobody = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, "fixed-wing", new ReportingDivision("k", "No Such Division"), ct);

        Assert.Equal(new OverallCashPerTransport(1, 100m), ground.Overall);
        Assert.Equal(new OverallCashPerTransport(0, 0m), nobody.Overall);
        Assert.Null(nobody.Overall?.CashPerTransport);
        Assert.All(nobody.Payers, p => Assert.Null(p.PercentFrom(nobody.Overall?.CashPerTransport)));
    }

    [Fact]
    public async Task Ground_is_a_separate_service_line()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport result = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Ground, null, null, TestContext.Current.CancellationToken);

        Assert.Equal((1, 100m), Pair(result, "commercial"));
        Assert.Equal(1, result.Payers.Sum(p => p.Transports));
    }

    [Fact]
    public async Task Cash_posted_after_the_chosen_period_is_left_out_until_that_period_is_chosen()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport november = await metric.GetCashPerTransportAsync(2026, new ApPeriod(2026, 11), ServiceLineFilter.Air, "rotor-wing", null, TestContext.Current.CancellationToken);

        // For Nov the cohorts are Jun 2025 to May 2026 and encounter 19's $400 has posted.
        Assert.Equal((3, 2400m), Pair(november, "commercial"));
    }

    [Fact]
    public async Task An_oversized_group_key_matches_nothing_instead_of_failing()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

        CashPerTransport result = await metric.GetCashPerTransportAsync(2026, Oct, ServiceLineFilter.Air, new string('x', 5000), null, TestContext.Current.CancellationToken);

        Assert.All(result.Payers, p => Assert.Equal(0, p.Transports));
    }

    private static (int Transports, decimal Cash) Pair(CashPerTransport result, string key)
    {
        PayerCashPerTransport payer = result.Payers.Single(p => p.Key == key);
        return (payer.Transports, payer.Cash);
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(CashPerTransportMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetCashPerTransportAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, division, TestContext.Current.CancellationToken);
        return mix.Payers.Sum(p => p.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        CashPerTransportMetric metric = await NewMetricAsync();

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
        CashPerTransportMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
