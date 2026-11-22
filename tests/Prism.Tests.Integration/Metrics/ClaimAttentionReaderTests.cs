using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Attention;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The Attention Check queries against a real PostgreSQL holding a table shaped like <c>reporting.trip_detail_snapshot</c>, including its patient columns.</summary>
public sealed class ClaimAttentionReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly ApPeriod October = new(2026, 10);

    private async Task<ClaimAttentionReader> NewReaderAsync(int maxRows = 10_000, FilingLimitOptions? limits = null)
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
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, run_number text, dos date NOT NULL,
                    status text NOT NULL, in_collections text NOT NULL, level_of_service text, prim_pay text, prim_pay_category text, vehicle text,
                    reporting_division text NOT NULL, denial_code text, denial_reason text, balance_due numeric NOT NULL,
                    patient text, ssn text, dob date, haddr text);

                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 1, 2026, 'FW-1', '2026-10-02', 'Submitted', 'No',  'Fixed Wing', 'Meridian Health Plan', 'Insurance', 'N418CV', 'Sky Harbor',  'CO-16', 'Missing documentation', 24600, 'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 2, 2026, 'FW-2', '2026-09-28', 'Submitted', 'No',  'Fixed Wing', 'Meridian Health Plan', 'Insurance', 'N418CV', 'Sky Harbor',  'CO-16', 'Missing documentation', 999,   'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 3, 2026, 'RW-3', '2026-10-10', 'Closed',    'No',  'Rotor Wing', 'Aetna',                'Insurance', 'N100AA', 'Sky Harbor',  'CO-45', 'Contractual',           0,     'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 4, 2026, 'RW-4', '2026-10-12', 'On_Hold',   'No',  'Rotor Wing', 'Aetna',                'Insurance', 'N100AA', 'Sky Harbor',  NULL,    NULL,                    100,   'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 5, 2026, 'G-5',  '2026-10-15', 'Submitted', 'Yes', 'ALS',        'Cigna',                'Insurance', 'M12',    'Ground One',  NULL,    NULL,                    50,    'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 6, 2026, 'G-6',  '2026-10-20', 'Submitted', 'No',  'ALS',        'Cigna',                'Insurance', 'M12',    'Ground One',  NULL,    NULL,                    75,    'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 7, 2026, '',     '2026-10-03', 'Submitted', 'No',  'Fixed Wing', 'Northstar Health',     'Medicare',  'N227CV', 'Regional',    'CO-50', 'Not medically necessary', 23600, 'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-11-21', 21, 777,  'X-1',  '2026-10-05', 'Submitted', 'No',  'Fixed Wing', 'Other Provider Plan',  'Insurance', 'N999ZZ', 'Elsewhere',   'CO-16', 'Other provider claim',  5000,  'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER'),
                    ('2026-10-01', 99, 2026, 'OLD-9', '2026-10-05', 'Submitted', 'No', 'Fixed Wing', 'Old Load Plan',      'Insurance', 'N000OL', 'Sky Harbor',  'CO-16', 'Old load',              8000,  'PATIENT-MARKER', 'SSN-MARKER', '1950-01-01', 'ADDR-MARKER');
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString, MaxRows = maxRows };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        FilingLimitOptions filing = limits ?? new FilingLimitOptions { Default = 365, ByPayerCategory = new() { ["Medicare"] = 90 } };
        return new ClaimAttentionReader(warehouse, new FakeProviderResolver(null), Options.Create(filing));
    }

    [Fact]
    public async Task Counts_follow_each_rule_in_the_period_from_the_latest_load_for_this_provider_only()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionCounts counts = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // Denied: 1 and 7 (a denial code and money owed). 3 has a code but nothing owed. 2 is September. 21 is another Provider. 99 is an old load.
        // Needs attention adds 4 (on hold) and 5 (in collections). All is every October claim.
        Assert.Equal(new DateOnly(2026, 11, 21), counts.DataThrough);
        Assert.Equal([("denied", 2), ("needs_attention", 4), ("all", 6)], counts.Counts.Select(c => (c.Key, c.Count)));
        Assert.Equal(48200m, counts.DeniedBalanceDue);
    }

    [Fact]
    public async Task Counts_can_be_limited_to_air_or_ground()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionCounts air = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Air, null, TestContext.Current.CancellationToken);
        AttentionCounts ground = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Ground, null, TestContext.Current.CancellationToken);

        Assert.Equal([2, 3, 4], air.Counts.Select(c => c.Count));
        Assert.Equal([0, 1, 2], ground.Counts.Select(c => c.Count));
    }

    [Fact]
    public async Task Counts_and_rows_can_be_limited_to_one_aircraft_type()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionCounts counts = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Both, "fixed-wing", TestContext.Current.CancellationToken);
        AttentionClaims rows = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("all")!, October, ServiceLineFilter.Both, "fixed-wing", TestContext.Current.CancellationToken);

        // Fixed Wing claims in October: 1 and 7 (2 is September, 21 is another Provider).
        Assert.Equal([2, 2, 2], counts.Counts.Select(c => c.Count));
        Assert.Equal(["7", "FW-1"], rows.Claims.Select(c => c.ClaimRef));
    }

    [Fact]
    public async Task A_period_with_no_claims_counts_zero_and_has_no_data_through()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionCounts counts = await reader.GetCountsAsync(2026, new ApPeriod(2024, 1), ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.All(counts.Counts, c => Assert.Equal(0, c.Count));
        Assert.Equal(0m, counts.DeniedBalanceDue);
        Assert.Null(counts.DataThrough);
    }

    [Fact]
    public async Task Denied_claims_come_back_soonest_filing_deadline_first_with_the_estimated_deadline()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionClaims result = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("denied")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // Claim 7 is Medicare with a 90-day limit, so it is due before claim 1, which has the 365-day default.
        Assert.Equal(new DateOnly(2026, 11, 21), result.DataThrough);
        Assert.Equal(["7", "FW-1"], result.Claims.Select(c => c.ClaimRef));
        AttentionClaim medicare = result.Claims[0];
        Assert.Equal(new DateOnly(2027, 1, 1), medicare.FilingDueDate);
        Assert.Equal(41, medicare.DaysLeft);
        Assert.Equal("Medicare", medicare.PayerCategory);
        Assert.Equal("Not medically necessary", medicare.DenialReason);
        Assert.Equal(23600m, medicare.BalanceDue);
        Assert.Equal(new DateOnly(2027, 10, 2), result.Claims[1].FilingDueDate);
    }

    [Fact]
    public async Task A_deadline_that_has_passed_shows_as_negative_days_left()
    {
        ClaimAttentionReader reader = await NewReaderAsync(limits: new FilingLimitOptions { Default = 30 });

        AttentionClaims result = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("denied")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        // 2026-10-02 + 30 days = 2026-11-01, which is 20 days before the data runs to.
        Assert.Equal(-20, result.Claims.Single(c => c.ClaimRef == "FW-1").DaysLeft);
    }

    [Fact]
    public async Task Needs_attention_adds_held_and_collections_claims_and_all_lists_everything_in_the_period()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionClaims attention = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("needs_attention")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);
        AttentionClaims all = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("all")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

        Assert.Equal(["7", "FW-1", "G-5", "RW-4"], attention.Claims.Select(c => c.ClaimRef).Order(StringComparer.Ordinal));
        Assert.Equal(4, attention.Claims.Count);
        Assert.Equal(6, all.Claims.Count);
        Assert.DoesNotContain(all.Claims, c => c.ClaimRef is "FW-2" or "X-1" or "OLD-9");
    }

    [Fact]
    public async Task No_patient_column_is_ever_read()
    {
        ClaimAttentionReader reader = await NewReaderAsync();

        AttentionClaims all = await reader.GetClaimsAsync(2026, AttentionCatalog.Find("all")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);
        string everything = string.Join('|', all.Claims);

        Assert.DoesNotContain("PATIENT-MARKER", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("SSN-MARKER", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("ADDR-MARKER", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("1950", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_claims_than_the_row_cap_is_refused_instead_of_truncated()
    {
        ClaimAttentionReader reader = await NewReaderAsync(maxRows: 3);

        await Assert.ThrowsAsync<WarehouseResultTooLargeException>(
            () => reader.GetClaimsAsync(2026, AttentionCatalog.Find("all")!, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken));
    }
}
