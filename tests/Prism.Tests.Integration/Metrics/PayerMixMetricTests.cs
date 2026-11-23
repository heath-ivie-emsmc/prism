using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The payer mix query against a real PostgreSQL holding a table shaped like <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class PayerMixMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<PayerMixMetric> NewMetricAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.trip_detail_snapshot;
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text,
                    prim_pay text, prim_pay_category text, dos date, reporting_division text);

                -- Provider 2026, latest load 2026-11-21. Oct is the chosen month, Sep the prior one.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 1,  2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-10-03'),
                    ('2026-11-21', 2,  2026, 'Rotor Wing', 'MEDICARE', 'Medicare', '2026-10-04'),
                    ('2026-11-21', 3,  2026, 'Rotor Wing', 'AARP MCR COMPLETE', 'Medicare Advantage', '2026-10-05'),
                    ('2026-11-21', 4,  2026, 'Rotor Wing', 'IV-VA COMMUNITY CLAIMS', 'Insurance', '2026-10-06'),
                    ('2026-11-21', 5,  2026, 'Rotor Wing', 'TRICARE WEST', 'Other Govt. Payers', '2026-10-07'),
                    ('2026-11-21', 6,  2026, 'Rotor Wing', 'CHAMPVA', 'Other Govt. Payers', '2026-10-08'),
                    ('2026-11-21', 7,  2026, 'Rotor Wing', 'STATE PROGRAM', 'Other Govt. Payers', '2026-10-09'),
                    ('2026-11-21', 8,  2026, 'Fixed Wing', 'OHP', 'Medicaid MCO', '2026-10-10'),
                    ('2026-11-21', 9,  2026, 'Fixed Wing', 'A HOSPITAL', 'Facility', '2026-10-11'),
                    ('2026-11-21', 10, 2026, 'Fixed Wing', 'BILL PATIENT', 'Patient', '2026-10-12'),
                    ('2026-11-21', 11, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-09-30'),
                    ('2026-11-21', 12, 2026, 'ALS', 'AETNA', 'Insurance', '2026-10-13'),
                    ('2026-11-21', 13, 2026, 'Rotor Wing', NULL, NULL, '2026-10-14'),
                    ('2026-11-21', 14, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-08-31'),
                    ('2026-11-21', 17, 2026, 'Rotor Wing', 'AVA HEALTH', 'Insurance', '2026-10-15'),
                    ('2026-11-21', 18, 2026, 'Rotor Wing', 'AETNA', 'Insurance', '2026-11-01'),
                    ('2026-11-21', 15, 777,  'Rotor Wing', 'MEDICARE', 'Medicare', '2026-10-04'),
                    ('2026-10-01', 1,  2026, 'Rotor Wing', 'MEDICARE', 'Medicare', '2026-10-03');
                

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
        return new PayerMixMetric(warehouse, new FakeProviderResolver(null));
    }

    private static int Current(PayerMix mix, string key) => mix.Shares.Single(s => s.Key == key).Current;

    [Fact]
    public async Task Air_transports_are_counted_by_payer_group_for_the_month_of_service_and_the_month_before()
    {
        PayerMixMetric metric = await NewMetricAsync();

        PayerMix mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("payer_mix", mix.Metric.Key);
        Assert.Equal(new ApPeriod(2026, 9), mix.PriorPeriod);
        Assert.Equal(["commercial", "medicare", "medicaid", "other-gov", "va", "facility", "patient", "not-recorded"], mix.Shares.Select(s => s.Key));
        // AETNA and AVA HEALTH are commercial. Medicare includes Medicare Advantage. Medicaid MCO is Medicaid.
        Assert.Equal(2, Current(mix, "commercial"));
        Assert.Equal(2, Current(mix, "medicare"));
        Assert.Equal(1, Current(mix, "medicaid"));
        Assert.Equal(1, Current(mix, "other-gov"));
        Assert.Equal(1, Current(mix, "facility"));
        Assert.Equal(1, Current(mix, "patient"));
        Assert.Equal(1, Current(mix, "not-recorded"));
        Assert.Equal(1, mix.Shares.Single(s => s.Key == "commercial").Prior);
    }

    [Fact]
    public async Task VA_CHAMPVA_and_TRICARE_payers_count_as_VA_whatever_financial_class_the_warehouse_gave_them()
    {
        PayerMixMetric metric = await NewMetricAsync();

        PayerMix mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        // Insurance (IV-VA), Other Govt. (TRICARE, CHAMPVA). A name that only contains the letters, like AVA HEALTH, is not VA.
        Assert.Equal(3, Current(mix, "va"));
    }

    [Fact]
    public async Task A_group_key_narrows_the_mix_to_one_aircraft_type_and_names_it()
    {
        PayerMixMetric metric = await NewMetricAsync();

        PayerMix mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, "fixed-wing", null, TestContext.Current.CancellationToken);

        Assert.Equal("fixed-wing", mix.GroupKey);
        Assert.Equal("Fixed Wing", mix.GroupName);
        Assert.Equal(3, mix.Shares.Sum(s => s.Current));
        Assert.Equal(0, Current(mix, "va"));
    }

    [Fact]
    public async Task The_service_line_decides_which_levels_of_service_count()
    {
        PayerMixMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        PayerMix ground = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Ground, null, null, ct);
        PayerMix both = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, null, ct);

        Assert.Equal(1, ground.Shares.Sum(s => s.Current));
        Assert.Equal("ALS", ground.GroupName);
        Assert.Equal(13, both.Shares.Sum(s => s.Current));
    }

    [Fact]
    public async Task Another_Providers_transports_and_older_loads_are_not_counted()
    {
        PayerMixMetric metric = await NewMetricAsync();

        PayerMix mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        // Provider 777's Medicare transport and the old load's Medicare row for encounter 1 would make this 3 if they leaked in.
        Assert.Equal(2, Current(mix, "medicare"));
    }

    [Fact]
    public async Task A_group_key_that_matches_nothing_gives_zeros_and_no_name()
    {
        PayerMixMetric metric = await NewMetricAsync();

        PayerMix mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, "no-such-group", null, TestContext.Current.CancellationToken);

        Assert.Null(mix.GroupName);
        Assert.All(mix.Shares, s => Assert.Equal(0, s.Current + s.Prior));
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(PayerMixMetric metric, ReportingDivision? division)
    {
        var mix = await metric.GetPayerMixAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, null, division, TestContext.Current.CancellationToken);
        return mix.Shares.Sum(s => s.Current + s.Prior);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        PayerMixMetric metric = await NewMetricAsync();

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
        PayerMixMetric metric = await NewMetricAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
