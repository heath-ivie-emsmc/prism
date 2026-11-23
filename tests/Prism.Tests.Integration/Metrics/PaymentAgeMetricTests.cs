using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The payment age query against a real PostgreSQL holding tables shaped like the <c>ods</c> ledger and <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class PaymentAgeMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<PaymentAgeMetric> NewMetricAsync()
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
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text,
                    prim_pay text, prim_pay_category text, dos date, reporting_division text);

                -- Provider 2026: Oct (1) with its adjustment period (2), Sep (3), and a Sep 2025 period (4), thirteen months back and outside the window. Provider 777 has an Oct period (9).
                INSERT INTO ods.accounting_period VALUES (1, 2026, '202610', false), (2, 2026, '-202610', true), (3, 2026, '202609', false), (4, 2026, '202509', false), (9, 777, '202610', true);
                INSERT INTO ods.credit VALUES (1, 1), (2, 2), (5, 5);

                -- 11 rotor Medicare, flown Oct. 12 rotor commercial, flown Apr. 13 fixed commercial, flown Apr. 14 fixed Medicaid, flown Sep.
                -- 15 fixed commercial, flown in 2020 (age capped). 16 rotor, no date of service. 17 ground ALS commercial, flown Oct. 18 rotor, in an old load only.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 11, 2026, 'Rotor Wing', 'MEDICARE', 'Medicare', '2026-10-03'),
                    ('2026-11-21', 12, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-04-10'),
                    ('2026-11-21', 13, 2026, 'Fixed Wing', 'CIGNA', 'Insurance', '2026-04-20'),
                    ('2026-11-21', 14, 2026, 'Fixed Wing', 'OHP', 'Medicaid MCO', '2026-09-02'),
                    ('2026-11-21', 15, 2026, 'Fixed Wing', 'AETNA', 'Insurance', '2020-01-01'),
                    ('2026-11-21', 16, 2026, 'Rotor Wing', 'AETNA', 'Insurance', NULL),
                    ('2026-11-21', 17, 2026, 'ALS', 'AETNA', 'Insurance', '2026-10-09'),
                    ('2026-11-21', 21, 777,  'Rotor Wing', 'MEDICARE', 'Medicare', '2026-10-04'),
                    ('2026-10-01', 18, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-01-01');

                INSERT INTO ods.encounter_transaction VALUES
                    (1, 11, 'Credit', 'Posting', 1, 100000),       -- $1,000.00 Oct, age 0
                    (2, 11, 'Credit', 'Posting', 2, -5000),        -- $50.00 refund in the Oct adjustment period, same month and age
                    (3, 12, 'Credit', 'Posting', 1, 20000),        -- $200.00 Oct, age 6
                    (4, 13, 'Credit', 'Posting', 1, 300000),       -- $3,000.00 Oct, age 6
                    (5, 14, 'Credit', 'Posting', 1, 40000),        -- $400.00 Oct, age 1
                    (6, 14, 'Credit', 'Posting', 3, 10000),        -- $100.00 Sep, age 0
                    (7, 15, 'Credit', 'Posting', 1, 70000),        -- $700.00 Oct, flown in 2020, capped at 24
                    (8, 16, 'Credit', 'Posting', 1, 90000),        -- no date of service, left out
                    (9, 17, 'Credit', 'Posting', 1, 50000),        -- ground
                    (10, 11, 'Credit', 'Old AR Import', 1, 999900),-- imported, excluded
                    (11, 11, 'Credit', 'Posting', 4, 888800),      -- Sep 2025, outside the window
                    (12, 21, 'Credit', 'Posting', 9, 5550000),     -- another Provider
                    (13, 12, 'Credit', 'Posting', 1, 10000);       -- two credit attributes: counted once, $100.00, age 6
                INSERT INTO ods.encounter_transaction_credit_attribute VALUES
                    (1, 1, 2), (2, 2, 5), (3, 3, 2), (4, 4, 2), (5, 5, 2), (6, 6, 2), (7, 7, 2), (8, 8, 2), (9, 9, 2),
                    (10, 10, 2), (11, 11, 2), (12, 12, 2), (13, 13, 2), (14, 13, 5);
                

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
        return new PaymentAgeMetric(warehouse, new FakeProviderResolver(null));
    }

    private static readonly ApPeriod Oct = new(2026, 10);

    [Fact]
    public async Task Air_cash_is_split_by_payer_posting_month_and_age()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("payment_age", result.Metric.Key);
        Assert.Equal(
            [
                ("medicaid", new ApPeriod(2026, 9), 0, 100m),
                ("commercial", Oct, 6, 3300m),
                ("commercial", Oct, 24, 700m),
                ("medicaid", Oct, 1, 400m),
                ("medicare", Oct, 0, 950m),
            ],
            result.Cells.Select(c => (c.PayerKey, c.PostingPeriod, c.AgeMonths, c.Cash)).OrderBy(c => c.Item2.Month).ThenBy(c => c.Item1, StringComparer.Ordinal).ThenBy(c => c.Item3));
    }

    [Fact]
    public async Task Cash_for_a_transport_is_counted_once_and_the_adjustment_period_joins_its_month()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        // Encounters 12 and 13 are both commercial, age 6: $200 + $100 (two credit attributes, counted once) + $3,000 = $3,300, two transports.
        PaymentAgeCell commercialSix = result.Cells.Single(c => c.PayerKey == "commercial" && c.AgeMonths == 6);
        Assert.Equal(3300m, commercialSix.Cash);
        Assert.Equal(2, commercialSix.Transports);
        // Encounter 11: $1,000 less the $50 refund posted in the adjustment period.
        Assert.Equal(950m, result.Cells.Single(c => c.PayerKey == "medicare").Cash);
    }

    [Fact]
    public async Task Old_payments_are_counted_at_the_maximum_age()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(IPaymentAgeMetric.MaxAgeMonths, result.Cells.Max(c => c.AgeMonths));
    }

    [Fact]
    public async Task A_group_key_narrows_the_cells_to_one_aircraft_type()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Air, "fixed-wing", null, TestContext.Current.CancellationToken);

        Assert.Equal("fixed-wing", result.GroupKey);
        Assert.DoesNotContain(result.Cells, c => c.PayerKey == "medicare");
        Assert.Equal(4200m, result.Cells.Sum(c => c.Cash));
    }

    [Fact]
    public async Task Ground_is_a_separate_service_line()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Ground, null, null, TestContext.Current.CancellationToken);

        PaymentAgeCell only = Assert.Single(result.Cells);
        Assert.Equal(("commercial", 0, 500m), (only.PayerKey, only.AgeMonths, only.Cash));
    }

    [Fact]
    public async Task Other_Providers_cash_the_old_import_cash_without_a_date_of_service_and_months_outside_the_window_never_appear()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Both, null, null, TestContext.Current.CancellationToken);

        // Everything except: Provider 777's $55,500, the $9,999 import, the $900 with no date of service and the $8,888 posted in Sep 2025.
        Assert.Equal(950m + 3300m + 700m + 400m + 100m + 500m, result.Cells.Sum(c => c.Cash));
    }

    [Fact]
    public async Task An_oversized_group_key_matches_nothing_instead_of_failing()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

        PaymentAge result = await metric.GetPaymentAgeAsync(2026, Oct, ServiceLineFilter.Air, new string('x', 5000), null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Cells);
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(PaymentAgeMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetPaymentAgeAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, division, TestContext.Current.CancellationToken);
        return mix.Cells.Sum(c => c.Transports);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        PaymentAgeMetric metric = await NewMetricAsync();

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
        PaymentAgeMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
