using Prism.Testing.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>The real warehouse path against a real PostgreSQL: the read-only session, the time limit, the row cap and the Provider filter.</summary>
public sealed class NpgsqlWarehouseTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<NpgsqlWarehouse> NewWarehouseAsync(int timeoutSeconds = 30, int maxRows = 100, ILogger<NpgsqlWarehouse>? logger = null)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS dw_payment (provider_id int NOT NULL, paid_month text NOT NULL, amount numeric NOT NULL);
                DELETE FROM dw_payment;
                INSERT INTO dw_payment VALUES (526, '2026-09', 100), (526, '2026-10', 250.5), (999, '2026-10', 7777);
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString, CommandTimeoutSeconds = timeoutSeconds, MaxRows = maxRows };
        return new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Microsoft.Extensions.Options.Options.Create(options), logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NpgsqlWarehouse>.Instance);
    }

    // A query that runs for a very long time without any function the policy bans (pg_sleep is one): it is cancelled by the time limit, not finished.
    private const string SlowSql = "SELECT count(*), @provider_id::int FROM generate_series(1, 100000000000)";

    private static WarehouseQuery Provider(string sql, int providerId, params (string, object?)[] more) =>
        new(sql, new[] { ("provider_id", (object?)providerId) }.Concat(more).ToDictionary(p => p.Item1, p => p.Item2));

    [Fact]
    public async Task A_query_returns_only_the_requested_providers_rows_with_bound_parameters()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync();

        var rows = await warehouse.QueryAsync(
            Provider("SELECT paid_month, amount FROM dw_payment WHERE provider_id = @provider_id AND paid_month >= @from ORDER BY paid_month", 526, ("from", "2026-10")),
            r => (Month: r.GetString(0), Amount: r.GetDecimal(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal([("2026-10", 250.5m)], rows);
    }

    [Fact]
    public async Task Another_providers_data_is_never_returned()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync();

        var rows = await warehouse.QueryAsync(
            Provider("SELECT amount FROM dw_payment WHERE provider_id = @provider_id", 12345),
            r => r.GetDecimal(0),
            TestContext.Current.CancellationToken);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_warehouse_session_is_read_only_even_if_a_write_gets_past_the_checks()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await NewWarehouseAsync();
        await using NpgsqlDataSource source = WarehouseDataSourceFactory.Create(new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString });
        await using NpgsqlCommand write = source.CreateCommand("INSERT INTO dw_payment VALUES (1, 'x', 1)");

        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync(ct));

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, ex.SqlState);
    }

    [Fact]
    public async Task A_write_that_bypasses_the_policy_is_refused_by_the_server_in_the_read_only_transaction_even_without_the_session_default()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await NewWarehouseAsync();
        // A data source with NO read-only session default and NO policy: only the explicit read-only transaction stands between the statement and the table.
        string raw = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Pooling = true, Options = "" }.ConnectionString;
        await using NpgsqlDataSource unprotected = new NpgsqlDataSourceBuilder(TestDatabaseGuard.Assert(raw)).Build();
        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(unprotected, Microsoft.Extensions.Options.Options.Create(options), Microsoft.Extensions.Logging.Abstractions.NullLogger<NpgsqlWarehouse>.Instance);

        // Prove the data source really would allow the write: a plain command on it succeeds against a scratch table.
        await using (NpgsqlCommand probe = unprotected.CreateCommand("CREATE TABLE IF NOT EXISTS scratch_probe (a int)"))
        {
            await probe.ExecuteNonQueryAsync(ct);
        }

        foreach (string write in new[]
        {
            "INSERT INTO dw_payment VALUES (@provider_id, 'x', 1) RETURNING provider_id",
            "DELETE FROM dw_payment WHERE provider_id = @provider_id RETURNING provider_id",
            "WITH d AS (UPDATE dw_payment SET amount = 0 WHERE provider_id = @provider_id RETURNING provider_id) SELECT provider_id FROM d",
            "CREATE TABLE should_not_exist AS SELECT @provider_id::int AS a",
        })
        {
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => warehouse.QueryWithoutValidationAsync(Provider(write, 526), r => r.GetInt32(0), ct));
            Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, ex.SqlState);
        }

        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(ct);
        await using var check = new NpgsqlCommand("SELECT (SELECT count(*) FROM dw_payment), (SELECT sum(amount) FROM dw_payment), to_regclass('should_not_exist') IS NULL", admin);
        await using NpgsqlDataReader reader = await check.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        Assert.Equal(3L, reader.GetInt64(0));
        Assert.Equal(8127.5m, reader.GetDecimal(1));
        Assert.True(reader.GetBoolean(2));
    }

    [Fact]
    public async Task A_statement_that_turns_the_session_read_only_default_off_cannot_open_a_write_either()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        NpgsqlWarehouse warehouse = await NewWarehouseAsync();

        // set_config would be refused by the policy, so this goes through the seam: the transaction was set read only first, and that cannot be undone inside it.
        await Assert.ThrowsAsync<PostgresException>(() => warehouse.QueryWithoutValidationAsync(
            Provider("SELECT set_config('transaction_read_only', 'off', false), set_config('default_transaction_read_only', 'off', false), @provider_id::int", 526), r => r.GetInt32(2), ct));
        await using NpgsqlDataSource fresh = WarehouseDataSourceFactory.Create(new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString });
        await using NpgsqlCommand write = fresh.CreateCommand("INSERT INTO dw_payment VALUES (1, 'x', 1)");
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync(ct));
        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, ex.SqlState);
    }

    [Fact]
    public async Task Every_read_runs_in_a_transaction_that_is_read_only_and_rolled_back()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync();

        var rows = await warehouse.QueryAsync(
            Provider("SELECT current_setting('transaction_read_only'), (SELECT count(*) FROM pg_stat_activity WHERE state = 'idle in transaction' AND application_name = 'prism-api'), @provider_id::int", 526),
            r => (ReadOnly: r.GetString(0), Others: r.GetInt64(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal("on", Assert.Single(rows).ReadOnly);
        // After the read the pooled connection is back with no transaction open (it was rolled back, not left idle in a transaction).
        var after = await warehouse.QueryAsync(
            Provider("SELECT (SELECT count(*) FROM pg_stat_activity WHERE state = 'idle in transaction' AND application_name = 'prism-api'), @provider_id::int", 526),
            r => r.GetInt64(0),
            TestContext.Current.CancellationToken);
        Assert.Equal(0L, Assert.Single(after));
    }

    [Fact]
    public async Task A_write_query_is_refused_before_it_reaches_the_database()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync();

        await Assert.ThrowsAsync<WarehouseQueryRejectedException>(() => warehouse.QueryAsync(
            Provider("DELETE FROM dw_payment WHERE provider_id = @provider_id", 526), r => r.GetInt32(0), TestContext.Current.CancellationToken));
        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM dw_payment WHERE provider_id = 526", admin);
        Assert.Equal(2L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_result_over_the_row_cap_fails_instead_of_filling_memory()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync(maxRows: 1);

        await Assert.ThrowsAsync<WarehouseResultTooLargeException>(() => warehouse.QueryAsync(
            Provider("SELECT amount FROM dw_payment WHERE provider_id = @provider_id", 526), r => r.GetDecimal(0), TestContext.Current.CancellationToken));
    }

    private sealed class CapturingLogger : ILogger<NpgsqlWarehouse>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task A_finished_query_logs_its_name_rows_and_time_and_never_the_sql_or_a_parameter_value()
    {
        var logger = new CapturingLogger();
        NpgsqlWarehouse warehouse = await NewWarehouseAsync(logger: logger);
        WarehouseQuery query = Provider("SELECT paid_month FROM dw_payment WHERE provider_id = @provider_id AND paid_month <> @marker", 526, ("marker", "PHI-MARKER-DOE-JOHN")) with { Name = "DemoQuery" };

        await warehouse.QueryAsync(query, r => r.GetString(0), TestContext.Current.CancellationToken);

        (LogLevel level, string message) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, level);
        Assert.Matches(@"^Warehouse query DemoQuery returned 2 rows in \d+ ms$", message);
        Assert.DoesNotContain("PHI-MARKER", message, StringComparison.Ordinal);
        Assert.DoesNotContain("dw_payment", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_logs_which_query_ran_how_long_and_the_sql_state()
    {
        var logger = new CapturingLogger();
        NpgsqlWarehouse warehouse = await NewWarehouseAsync(timeoutSeconds: 1, logger: logger);

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => warehouse.QueryAsync(
            Provider(SlowSql, 526) with { Name = "SlowQuery" }, r => r.GetInt32(1), TestContext.Current.CancellationToken));

        (LogLevel level, string message) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Matches(@"^The data warehouse is unavailable for query SlowQuery after \d+ ms \(SQL state (57014|none)\)$", message);
    }

    [Fact]
    public async Task A_database_error_in_a_query_logs_only_its_name_time_and_sql_state_never_the_message()
    {
        var logger = new CapturingLogger();
        NpgsqlWarehouse warehouse = await NewWarehouseAsync(logger: logger);

        await Assert.ThrowsAsync<PostgresException>(() => warehouse.QueryAsync(
            Provider("SELECT 1 / (@provider_id::int - 526) + length(@marker)", 526, ("marker", "PHI-MARKER-DOE-JOHN")) with { Name = "BrokenQuery" }, r => r.GetInt32(0), TestContext.Current.CancellationToken));

        (LogLevel level, string message) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Matches(@"^The data warehouse refused query BrokenQuery after \d+ ms \(SQL state 22012\)$", message);
        Assert.DoesNotContain("division", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PHI-MARKER", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slow_query_is_cancelled_at_the_time_limit()
    {
        NpgsqlWarehouse warehouse = await NewWarehouseAsync(timeoutSeconds: 1);

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => warehouse.QueryAsync(
            Provider(SlowSql, 526), r => r.GetInt32(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_warehouse_that_cannot_be_reached_reports_unavailable_without_the_connection_string()
    {
        var options = new PrismWarehouseOptions { ConnectionString = "Host=127.0.0.1;Port=1;Database=dw;Username=u;Password=NeverShowThis1;Timeout=2" };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Microsoft.Extensions.Options.Options.Create(options), Microsoft.Extensions.Logging.Abstractions.NullLogger<NpgsqlWarehouse>.Instance);

        WarehouseUnavailableException ex = await Assert.ThrowsAsync<WarehouseUnavailableException>(() => warehouse.QueryAsync(
            Provider("SELECT 1 WHERE @provider_id > 0", 1), r => r.GetInt32(0), TestContext.Current.CancellationToken));

        Assert.DoesNotContain("NeverShowThis1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unconfigured_warehouse_says_it_is_unavailable()
    {
        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => new UnconfiguredWarehouse().QueryAsync(
            Provider("SELECT 1 WHERE @provider_id > 0", 1), r => r.GetInt32(0), TestContext.Current.CancellationToken));
    }
}
