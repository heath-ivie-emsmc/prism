using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The clean claim rate query against a real PostgreSQL holding a table shaped like <c>reporting.kpi_clean_claim_rate__prov</c>.</summary>
public sealed class CleanClaimRateMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<CleanClaimRateMetric> NewMetricAsync()
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
                DROP TABLE IF EXISTS reporting.kpi_clean_claim_rate__prov;
                CREATE TABLE reporting.kpi_clean_claim_rate__prov (
                    is_current boolean, snapshot_date date, provider_id int, dos_year_month char(7), clean_claims int, rejected_claims int, total_claims int, pod_id int);

                -- Provider 2026: a load on 2026-04-07 and a newer one on 2026-05-07. Only the newer one counts.
                INSERT INTO reporting.kpi_clean_claim_rate__prov VALUES
                    (false, '2026-04-07', 2026, '2026-01', 1, 1, 2, NULL),
                    (false, '2026-05-07', 2026, '2026-01', 945, 0, 945, NULL),
                    (false, '2026-05-07', 2026, '2026-02', 2767, 1, 2768, NULL),
                    (false, '2026-05-07', 2026, '2026-03', 1849, 59, 1908, NULL),
                    (false, '2026-05-07', 2026, '2026-04', 0, 0, 0, NULL),
                    -- A later month than the one asked for, and one before the range.
                    (false, '2026-05-07', 2026, '2026-05', 10, 0, 10, NULL),
                    (false, '2025-05-07', 2026, '2025-12', 5, 5, 10, NULL),
                    -- Provider 300 has pods and no row for the whole Provider: they add up. Provider 400 has both: only the whole row counts.
                    (false, '2026-05-07', 300, '2026-03', 80, 20, 100, 1),
                    (false, '2026-05-07', 300, '2026-03', 40, 10, 50, 2),
                    (false, '2026-05-07', 400, '2026-03', 90, 10, 100, NULL),
                    (false, '2026-05-07', 400, '2026-03', 80, 20, 100, 1),
                    (false, '2026-05-07', 400, '2026-03', 10, 0, 10, 2),
                    (false, '2026-05-07', 777, '2026-03', 1, 99, 100, NULL);
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
        return new CleanClaimRateMetric(warehouse, new FakeProviderResolver(null));
    }

    [Fact]
    public async Task Months_come_back_oldest_first_from_the_latest_load_with_the_rate_worked_out()
    {
        CleanClaimRateMetric metric = await NewMetricAsync();

        CleanClaimRate rate = await metric.GetRateAsync(2026, new ApPeriod(2026, 4), 4, TestContext.Current.CancellationToken);

        Assert.Equal("clean_claim_rate", rate.Metric.Key);
        Assert.Equal(new DateOnly(2026, 5, 7), rate.SnapshotDate);
        Assert.Equal(
            [(new ApPeriod(2026, 1), 945, 0, 945, 1m), (new ApPeriod(2026, 2), 2767, 1, 2768, 0.9996m), (new ApPeriod(2026, 3), 1849, 59, 1908, 0.9691m), (new ApPeriod(2026, 4), 0, 0, 0, (decimal?)null)],
            rate.Points.Select(p => (p.Period, p.CleanClaims, p.RejectedClaims, p.TotalClaims, p.Rate)));
    }

    [Fact]
    public async Task Months_after_the_end_and_before_the_start_of_the_range_are_left_out()
    {
        CleanClaimRateMetric metric = await NewMetricAsync();

        CleanClaimRate rate = await metric.GetRateAsync(2026, new ApPeriod(2026, 3), 2, TestContext.Current.CancellationToken);

        Assert.Equal([new ApPeriod(2026, 2), new ApPeriod(2026, 3)], rate.Points.Select(p => p.Period));
    }

    [Fact]
    public async Task The_newest_point_can_be_older_than_the_period_asked_for()
    {
        CleanClaimRateMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        CleanClaimRate behind = await metric.GetRateAsync(2026, new ApPeriod(2026, 8), 6, ct);
        CleanClaimRate none = await metric.GetRateAsync(2026, new ApPeriod(2026, 10), 3, ct);

        Assert.Equal(new ApPeriod(2026, 8), behind.EndPeriod);
        Assert.Equal(new ApPeriod(2026, 5), behind.Points[^1].Period);
        Assert.Empty(none.Points);
        Assert.Null(none.SnapshotDate);
    }

    [Fact]
    public async Task Pods_add_up_to_the_month_unless_the_table_has_a_row_for_the_whole_Provider()
    {
        CleanClaimRateMetric metric = await NewMetricAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        CleanClaimPoint onlyPods = (await metric.GetRateAsync(300, new ApPeriod(2026, 3), 1, ct)).Points.Single();
        CleanClaimPoint both = (await metric.GetRateAsync(400, new ApPeriod(2026, 3), 1, ct)).Points.Single();

        Assert.Equal((120, 30, 150), (onlyPods.CleanClaims, onlyPods.RejectedClaims, onlyPods.TotalClaims));
        Assert.Equal((90, 10, 100), (both.CleanClaims, both.RejectedClaims, both.TotalClaims));
    }

    [Fact]
    public async Task Another_Providers_claims_are_not_counted()
    {
        CleanClaimRateMetric metric = await NewMetricAsync();

        CleanClaimRate rate = await metric.GetRateAsync(2026, new ApPeriod(2026, 3), 1, TestContext.Current.CancellationToken);

        Assert.Equal(1908, rate.Points.Single().TotalClaims);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(999, 24)]
    public async Task The_number_of_months_is_kept_within_bounds_before_it_reaches_a_query(int asked, int used)
    {
        CleanClaimRateMetric metric = await NewMetricAsync();

        CleanClaimRate rate = await metric.GetRateAsync(2026, new ApPeriod(2026, 3), asked, TestContext.Current.CancellationToken);

        Assert.Equal(used, rate.Months);
    }
}
