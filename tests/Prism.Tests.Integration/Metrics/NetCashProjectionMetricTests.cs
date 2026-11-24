using Prism.Testing.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The projection query against a real PostgreSQL holding a table shaped like <c>reporting.forecast_cash_per_trip</c>.</summary>
public sealed class NetCashProjectionMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<NetCashProjectionMetric> NewMetricAsync(IReadOnlyDictionary<int, int>? providerMap = null)
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
                DROP TABLE IF EXISTS reporting.forecast_cash_per_trip;
                CREATE TABLE reporting.forecast_cash_per_trip (
                    is_current boolean NOT NULL, snapshot_date date NOT NULL, provider_id int NOT NULL, month_index date NOT NULL,
                    forecasted_ar_cash numeric, forecasted_budget_cash numeric, total_projected_cash numeric, is_air boolean NOT NULL);
                -- An old load, ignored.
                INSERT INTO reporting.forecast_cash_per_trip VALUES (false, '2026-03-31', 2026, '2026-11-01', 1, 1, 1, true);
                -- Current load. Nov: air 100 (ar 90, budget 130), ground 50 (ar 40, budget 60). Dec: air only, no budget. Jan, Feb: air only.
                INSERT INTO reporting.forecast_cash_per_trip VALUES
                    (true, '2026-11-05', 2026, '2026-09-01', 5, 5, 5, true),
                    (true, '2026-11-05', 2026, '2026-10-01', 7, 7, 7, true),
                    (true, '2026-11-05', 2026, '2026-11-01', 90, 130, 100, true),
                    (true, '2026-11-05', 2026, '2026-11-01', 40, 60, 50, false),
                    (true, '2026-11-05', 2026, '2026-12-01', 80, NULL, 85, true),
                    (true, '2026-11-05', 2026, '2027-01-01', 70, 90, 75, true),
                    (true, '2026-11-05', 2026, '2027-02-01', 60, 60, 60, true),
                    (true, '2026-11-05', 2026, '2027-03-01', 1, 1, NULL, true);
                -- Another Provider, never returned for 2026.
                INSERT INTO reporting.forecast_cash_per_trip VALUES (true, '2026-11-05', 777, '2026-11-01', 9999, 9999, 9999, true);
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
        return new NetCashProjectionMetric(warehouse, new FakeProviderResolver(providerMap));
    }

    [Fact]
    public async Task It_returns_the_three_months_after_the_chosen_period_with_a_range_and_sums()
    {
        NetCashProjectionMetric metric = await NewMetricAsync();

        NetCashProjection projection = await metric.GetProjectionAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        // Nov adds air and ground: total 150, low = min(150, 130, 190) = 130, high = max(...) = 190. Rows for Sep and Oct are before the window.
        Assert.Equal(
            [("202611", 150m, 130m, 190m), ("202612", 85m, 80m, 85m), ("202701", 75m, 70m, 90m)],
            projection.Months.Select(m => (m.Period.ToString(), m.Value, m.Low, m.High)));
        Assert.Equal(310m, projection.Total);
        Assert.Equal(new DateOnly(2026, 11, 5), projection.SnapshotDate);
        Assert.Equal("net_cash_projection", projection.Metric.Key);
    }

    [Fact]
    public async Task The_two_components_are_summed_as_read_and_null_when_the_warehouse_has_none()
    {
        NetCashProjectionMetric metric = await NewMetricAsync();

        NetCashProjection projection = await metric.GetProjectionAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        // Nov: existing 90 + 40, future 130 + 60, value 150. The components (130 and 190) do not add up to the value and are reported as they are. Dec has no budget forecast.
        Assert.Equal(
            [(130m, 190m), (80m, null), (70m, 90m)],
            projection.Months.Select(m => (m.FromExistingTrips, m.FromFutureTrips)));
    }

    [Fact]
    public async Task The_service_line_picks_air_or_ground_rows()
    {
        NetCashProjectionMetric metric = await NewMetricAsync();

        NetCashProjection air = await metric.GetProjectionAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Air, TestContext.Current.CancellationToken);
        NetCashProjection ground = await metric.GetProjectionAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Ground, TestContext.Current.CancellationToken);

        Assert.Equal(100m, air.Months[0].Value);
        Assert.Equal(50m, Assert.Single(ground.Months).Value);
    }

    [Fact]
    public async Task A_period_with_no_forecast_after_it_gives_no_months_and_rows_without_a_projected_amount_are_left_out()
    {
        NetCashProjectionMetric metric = await NewMetricAsync();

        NetCashProjection projection = await metric.GetProjectionAsync(2026, new ApPeriod(2027, 2), ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        Assert.Empty(projection.Months);
        Assert.Equal(0m, projection.Total);
    }

    [Fact]
    public async Task Another_providers_rows_are_never_returned()
    {
        NetCashProjectionMetric metric = await NewMetricAsync();

        NetCashProjection other = await metric.GetProjectionAsync(31337, new ApPeriod(2026, 10), ServiceLineFilter.Both, TestContext.Current.CancellationToken);
        NetCashProjection mine = await metric.GetProjectionAsync(2026, new ApPeriod(2026, 10), ServiceLineFilter.Both, TestContext.Current.CancellationToken);

        Assert.Empty(other.Months);
        Assert.DoesNotContain(mine.Months, m => m.Value == 9999m);
    }

    [Fact]
    public async Task The_development_provider_replaces_the_requested_provider_only_when_set()
    {
        NetCashProjectionMetric standIn = await NewMetricAsync(new Dictionary<int, int> { [526] = 2026 });

        NetCashProjection projection = await standIn.GetProjectionAsync(526, new ApPeriod(2026, 10), ServiceLineFilter.Air, TestContext.Current.CancellationToken);

        Assert.Equal(100m, projection.Months[0].Value);
    }
}
