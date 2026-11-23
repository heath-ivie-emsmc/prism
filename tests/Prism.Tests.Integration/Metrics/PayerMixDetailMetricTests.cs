using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The payer detail query against a real PostgreSQL holding a table shaped like <c>reporting.trip_detail_snapshot</c>.</summary>
public sealed class PayerMixDetailMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly string LongName = new('X', 500);

    private async Task<(PayerMixDetailMetric Detail, PayerMixMetric Mix)> NewMetricsAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                $$"""
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.trip_detail_snapshot;
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text,
                    prim_pay text, prim_pay_category text, dos date, reporting_division text);

                -- Provider 2026, latest load 2026-11-21. Oct is the chosen month, Sep the prior one.
                -- Twelve commercial payers: PAYER 01 has 12 transports in Oct, PAYER 12 has 1.
                INSERT INTO reporting.trip_detail_snapshot
                SELECT '2026-11-21', 1000 + p * 100 + n, 2026, 'Rotor Wing', 'PAYER ' || lpad(p::text, 2, '0'), 'Insurance', DATE '2026-10-01' + n, NULL
                FROM generate_series(1, 12) p, generate_series(1, 13 - p) n;
                -- PAYER 12 has five more in Sep, so it ranks on both months together: above PAYER 08..11 even though its Oct count is lowest.
                INSERT INTO reporting.trip_detail_snapshot
                SELECT '2026-11-21', 5000 + n, 2026, 'Rotor Wing', 'PAYER 12', 'Insurance', DATE '2026-09-01' + n, NULL FROM generate_series(1, 5) n;

                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 1, 2026, 'Fixed Wing', 'MEDICARE', 'Medicare', '2026-10-04', NULL),
                    ('2026-11-21', 2, 2026, 'Fixed Wing', 'MEDICARE', 'Medicare', '2026-10-05', NULL),
                    ('2026-11-21', 3, 2026, 'Fixed Wing', 'AARP MCR COMPLETE', 'Medicare Advantage', '2026-10-06', NULL),
                    ('2026-11-21', 4, 2026, 'Fixed Wing', 'MEDICARE', 'Medicare', '2026-09-06', NULL),
                    ('2026-11-21', 5, 2026, 'Rotor Wing', 'IV-VA COMMUNITY CLAIMS', 'Insurance', '2026-10-06', NULL),
                    ('2026-11-21', 6, 2026, 'Rotor Wing', 'IV-VA COMMUNITY CLAIMS', 'Insurance', '2026-10-07', NULL),
                    ('2026-11-21', 7, 2026, 'Rotor Wing', 'TRICARE WEST', 'Other Govt. Payers', '2026-10-08', NULL),
                    ('2026-11-21', 8, 2026, 'Rotor Wing', 'TRICARE WEST', 'Other Govt. Payers', '2026-09-08', NULL),
                    ('2026-11-21', 9, 2026, 'Rotor Wing', 'BILL PATIENT', 'Patient', '2026-10-09', NULL),
                    ('2026-11-21', 10, 2026, 'Rotor Wing', 'A HOSPITAL', 'Facility', '2026-10-10', NULL),
                    ('2026-11-21', 11, 2026, 'Rotor Wing', NULL, NULL, '2026-10-11', NULL),
                    -- Ground transports with hostile payer names, all commercial.
                    ('2026-11-21', 20, 2026, 'ALS', '=HYPERLINK("http://evil","x")', 'Insurance', '2026-10-13', NULL),
                    ('2026-11-21', 21, 2026, 'ALS', '{{LongName}}', 'Insurance', '2026-10-13', NULL),
                    ('2026-11-21', 22, 2026, 'ALS', 'A' || chr(9) || chr(10) || 'B   C', 'Insurance', '2026-10-13', NULL),
                    ('2026-11-21', 23, 2026, 'ALS', NULL, 'Insurance', '2026-10-13', NULL),
                    ('2026-11-21', 24, 2026, 'ALS', '    ', 'Insurance', '2026-10-13', NULL),
                    ('2026-11-21', 25, 2026, 'ALS', 'Other', 'Insurance', '2026-10-13', NULL),
                    -- Another Provider, and an older load of this one: neither may be counted.
                    ('2026-11-21', 30, 777, 'Rotor Wing', 'PAYER 01', 'Insurance', '2026-10-04', 'Other Provider - Base'),
                    ('2026-11-21', 31, 777, 'Rotor Wing', 'LEAKED PAYER', 'Insurance', '2026-10-04', 'Other Provider - Base'),
                    ('2026-10-01', 32, 2026, 'Rotor Wing', 'OLD LOAD PAYER', 'Insurance', '2026-10-04', NULL);

                -- Even encounters are the Rotor division, odd ones the Fixed division (filed under a legacy "- Old AR" name that counts as the current one).
                UPDATE reporting.trip_detail_snapshot SET reporting_division = CASE WHEN encounter_id % 2 = 0 THEN 'Mercy Flights - Rotor' ELSE 'Mercy Flights - Fixed - Old AR 2019' END WHERE provider_id = 2026;
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
        var resolver = new FakeProviderResolver(null);
        return (new PayerMixDetailMetric(warehouse, resolver), new PayerMixMetric(warehouse, resolver));
    }

    private static readonly ApPeriod October = new(2026, 10);

    private static Task<PayerMixDetail> DetailAsync(PayerMixDetailMetric metric, string payer, ServiceLineFilter line = ServiceLineFilter.Air, string? group = null, ReportingDivision? division = null) =>
        metric.GetDetailAsync(2026, October, line, group, payer, division, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_top_ten_payers_by_both_months_are_listed_and_the_rest_are_one_Other_row()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail detail = await DetailAsync(metric, "commercial");

        Assert.Equal(11, detail.Payers.Count);
        Assert.Equal(["PAYER 01", "PAYER 02", "PAYER 03", "PAYER 04", "PAYER 05", "PAYER 06", "PAYER 07", "PAYER 12", "PAYER 08", "PAYER 09", "Other"], detail.Payers.Select(p => p.Name));
        Assert.Equal((12, 0), (detail.Payers[0].Current, detail.Payers[0].Prior));
        Assert.Equal((1, 5), (detail.Payers[7].Current, detail.Payers[7].Prior));
        // PAYER 10 (3) and PAYER 11 (2) are what is left.
        Assert.Equal(new PayerMixDetailPayer("Other", 5, 0), detail.Payers[^1]);
        Assert.Equal(("commercial", "Commercial", "payer_mix_detail", new ApPeriod(2026, 9)), (detail.PayerGroupKey, detail.PayerGroupName, detail.Metric.Key, detail.PriorPeriod));
    }

    [Theory]
    [InlineData("commercial")]
    [InlineData("medicare")]
    [InlineData("va")]
    [InlineData("other-gov")]
    [InlineData("medicaid")]
    public async Task The_rows_add_up_to_the_payer_mix_share_of_the_group(string payer)
    {
        (PayerMixDetailMetric metric, PayerMixMetric mix) = await NewMetricsAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        PayerMixDetail detail = await DetailAsync(metric, payer, ServiceLineFilter.Both);
        PayerMix shares = await mix.GetPayerMixAsync(2026, October, ServiceLineFilter.Both, null, null, ct);

        PayerMixShare share = shares.Shares.Single(s => s.Key == payer);
        Assert.Equal((share.Current, share.Prior), (detail.Payers.Sum(p => p.Current), detail.Payers.Sum(p => p.Prior)));
    }

    [Fact]
    public async Task Medicare_includes_Medicare_Advantage_by_payer_name_and_VA_takes_payers_by_name_whatever_their_class()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail medicare = await DetailAsync(metric, "medicare");
        PayerMixDetail va = await DetailAsync(metric, "va");

        Assert.Equal(new[] { ("MEDICARE", 2, 1), ("AARP MCR COMPLETE", 1, 0) }.Order(), medicare.Payers.Select(p => (p.Name, p.Current, p.Prior)).Order());
        Assert.Equal(new[] { ("IV-VA COMMUNITY CLAIMS", 2, 0), ("TRICARE WEST", 1, 1) }.Order(), va.Payers.Select(p => (p.Name, p.Current, p.Prior)).Order());
    }

    [Fact]
    public async Task A_group_with_no_transports_is_empty_and_never_borrows_another_groups_payers()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail detail = await DetailAsync(metric, "medicaid");

        Assert.Empty(detail.Payers);
    }

    [Fact]
    public async Task A_level_of_service_group_key_narrows_the_payers_and_names_the_group()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail fixedWing = await DetailAsync(metric, "medicare", ServiceLineFilter.Air, "fixed-wing");
        PayerMixDetail rotor = await DetailAsync(metric, "medicare", ServiceLineFilter.Air, "rotor-wing");
        PayerMixDetail none = await DetailAsync(metric, "medicare", ServiceLineFilter.Air, "no-such-group");

        Assert.Equal(("fixed-wing", "Fixed Wing"), (fixedWing.GroupKey, fixedWing.GroupName));
        Assert.Equal(4, fixedWing.Payers.Sum(p => p.Current + p.Prior));
        Assert.Empty(rotor.Payers);
        Assert.Equal("Rotor Wing", rotor.GroupName);
        Assert.Empty(none.Payers);
        Assert.Null(none.GroupName);
    }

    [Fact]
    public async Task The_service_line_decides_which_levels_of_service_count()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail air = await DetailAsync(metric, "commercial", ServiceLineFilter.Air);
        PayerMixDetail ground = await DetailAsync(metric, "commercial", ServiceLineFilter.Ground);

        Assert.DoesNotContain(air.Payers, p => p.Name.Contains("HYPERLINK", StringComparison.Ordinal));
        Assert.DoesNotContain(ground.Payers, p => p.Name.StartsWith("PAYER", StringComparison.Ordinal));
        Assert.NotEmpty(ground.Payers);
    }

    [Fact]
    public async Task Another_Providers_transports_and_older_loads_are_not_counted()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail detail = await DetailAsync(metric, "commercial", ServiceLineFilter.Both);

        Assert.DoesNotContain(detail.Payers, p => p.Name is "LEAKED PAYER" or "OLD LOAD PAYER");
        // Provider 777's 1 PAYER 01 transport would make this 13.
        Assert.Equal(12, detail.Payers.Single(p => p.Name == "PAYER 01").Current);
    }

    [Fact]
    public async Task A_hostile_payer_name_is_cleaned_capped_and_never_changes_the_shape_of_the_answer()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        PayerMixDetail detail = await DetailAsync(metric, "commercial", ServiceLineFilter.Ground);

        string[] names = [.. detail.Payers.Select(p => p.Name)];
        Assert.All(names, name => Assert.InRange(name.Length, 1, IPayerMixDetailMetric.MaxNameLength));
        Assert.Contains(new string('X', IPayerMixDetailMetric.MaxNameLength), names);
        // Control characters and runs of spaces become one space. Blank and missing names are one "Unknown payer".
        Assert.Contains("A B C", names);
        Assert.Equal(2, detail.Payers.Single(p => p.Name == "Unknown payer").Current);
        // A formula is only text here. It is passed through as data, and nothing in it can add a row or a field.
        Assert.Contains("=HYPERLINK(\"http://evil\",\"x\")", names);
        // A real payer called Other is not the roll-up row.
        Assert.Contains("Other payer", names);
        Assert.DoesNotContain("Other", names);
        Assert.Equal(6, detail.Payers.Sum(p => p.Current));
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("facility")]
    [InlineData("not-recorded")]
    [InlineData("nope")]
    [InlineData("")]
    public async Task A_group_without_payer_detail_is_refused_before_the_warehouse_is_asked(string payer)
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => DetailAsync(metric, payer));
    }

    private static readonly ReportingDivision Rotor = new("rotor", "Mercy Flights - Rotor");
    private static readonly ReportingDivision Fixed = new("fixed", "Mercy Flights - Fixed");

    private static async Task<int> TotalAsync(PayerMixDetailMetric metric, ReportingDivision? division)
    {
        PayerMixDetail detail = await DetailAsync(metric, "commercial", ServiceLineFilter.Both, null, division);
        return detail.Payers.Sum(p => p.Current + p.Prior);
    }

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

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
        (PayerMixDetailMetric metric, _) = await NewMetricsAsync();

        int total = await TotalAsync(metric, new ReportingDivision("k", name));

        // The empty name is "no division": it is the unfiltered figure, never an error and never a match on an empty division.
        Assert.Equal(name.Length == 0 ? await TotalAsync(metric, null) : 0, total);
    }
}
