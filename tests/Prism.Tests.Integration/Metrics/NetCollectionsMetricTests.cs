using Prism.Testing.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
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

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), 4, ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        // 202504..202507. Two rows in 202506 add up. The old load's rows are never read.
        Assert.Equal([("202504", 400m), ("202505", 500m), ("202506", 650m), ("202507", 700m)], series.Points.Select(p => (p.Period.ToString(), p.Value)));
        Assert.Equal(new DateOnly(2026, 11, 5), series.SnapshotDate);
        Assert.Equal("net_collections", series.Metric.Key);
    }

    [Fact]
    public async Task Months_with_no_data_or_no_amount_are_left_out_never_shown_as_zero()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 10), 3, ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        Assert.Equal([("202509", 900m)], series.Points.Select(p => (p.Period.ToString(), p.Value)));
    }

    [Fact]
    public async Task Another_providers_rows_are_never_returned()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        NetCollectionsSeries series = await metric.GetSeriesAsync(2026, new ApPeriod(2025, 11), 24, ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(series.Points, p => p.Value is 99999m or 88888m);
        Assert.Empty((await metric.GetSeriesAsync(31337, new ApPeriod(2025, 11), 24, ServiceLineFilter.Both, TestContext.Current.CancellationToken)).Points);
        Assert.Empty(await metric.GetPeriodsAsync(31337, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_development_provider_replaces_the_requested_provider_only_when_set()
    {
        NetCollectionsMetric standIn = await NewMetricAsync(new Dictionary<int, int> { [526] = 2026 });

        NetCollectionsSeries series = await standIn.GetSeriesAsync(526, new ApPeriod(2025, 7), 1, ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        Assert.Equal(700m, Assert.Single(series.Points).Value);
    }

    [Fact]
    public async Task A_window_that_is_too_long_or_empty_is_refused()
    {
        NetCollectionsMetric metric = await NewMetricAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), 0, ServiceLineFilter.Both, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => metric.GetSeriesAsync(2026, new ApPeriod(2025, 7), INetCollectionsMetric.MaxMonths + 1, ServiceLineFilter.Both, TestContext.Current.CancellationToken));
    }
}
