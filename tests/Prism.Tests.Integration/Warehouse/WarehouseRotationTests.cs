using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Testing.Common;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>
/// AGENTS.md section 9 for a rotated warehouse login: the changed connection string gets the same read-only proof as the startup one, before any query uses it.
/// Roles stand in for warehouse logins in a throwaway container; every connection comes from <see cref="PostgresFixture"/> through <see cref="TestDatabaseGuard"/>.
/// </summary>
public sealed class WarehouseRotationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = "NeverShowThis-Pw1";
    private static int roleCounter;

    private async Task AdminAsync(string sql)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<string> LoginAsync(string kind, string extra = "")
    {
        await AdminAsync("CREATE SCHEMA IF NOT EXISTS reporting; CREATE TABLE IF NOT EXISTS reporting.rotation_probe (provider_id int NOT NULL);");
        string role = $"rot_{kind}_{Interlocked.Increment(ref roleCounter)}";
        await AdminAsync(
            $"""
            CREATE ROLE {role} LOGIN PASSWORD '{Password}';
            GRANT CONNECT ON DATABASE {new NpgsqlConnectionStringBuilder(postgres.ConnectionString).Database} TO {role};
            GRANT USAGE ON SCHEMA reporting TO {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO {role};
            {extra.Replace("{role}", role, StringComparison.Ordinal)}
            """);
        return new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Username = role, Password = Password }.ConnectionString;
    }

    private static RotatingWarehouseConnections ConnectionsFor(string startup, Func<string> current)
    {
        var options = new PrismWarehouseOptions { ConnectionString = startup };
        return new RotatingWarehouseConnections(
            current,
            options,
            WarehouseDataSourceFactory.Create(options),
            deployed: false,
            (dataSource, candidate, ct) => new WarehouseWriteProtectionCheck(dataSource, Options.Create(candidate), NullLogger<WarehouseWriteProtectionCheck>.Instance).VerifyAsync(ct),
            TimeProvider.System,
            NullLogger.Instance);
    }

    private static async Task<string> CurrentUserAsync(RotatingWarehouseConnections connections)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using WarehouseDataSourceLease lease = await connections.AcquireAsync(ct);
        await using NpgsqlCommand command = lease.DataSource.CreateCommand("SELECT current_user");
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    [Fact]
    public async Task A_rotated_read_only_login_is_used_by_the_next_query()
    {
        string first = await LoginAsync("first");
        string second = await LoginAsync("second");
        string wanted = first;
        using RotatingWarehouseConnections connections = ConnectionsFor(first, () => wanted);

        string before = await CurrentUserAsync(connections);
        wanted = second;
        string after = await CurrentUserAsync(connections);

        Assert.Equal(new NpgsqlConnectionStringBuilder(first).Username, before);
        Assert.Equal(new NpgsqlConnectionStringBuilder(second).Username, after);
    }

    [Theory]
    [InlineData("GRANT INSERT ON ALL TABLES IN SCHEMA reporting TO {role};")]
    [InlineData("GRANT CREATE ON SCHEMA reporting TO {role};")]
    [InlineData("ALTER ROLE {role} CREATEDB;")]
    public async Task A_rotated_login_that_can_write_is_refused_and_the_message_hides_the_password(string extra)
    {
        string good = await LoginAsync("good");
        string bad = await LoginAsync("bad", extra);
        string wanted = good;
        using RotatingWarehouseConnections connections = ConnectionsFor(good, () => wanted);
        _ = await CurrentUserAsync(connections);

        wanted = bad;
        WarehouseUnavailableException ex = await Assert.ThrowsAsync<WarehouseUnavailableException>(() => CurrentUserAsync(connections));
        Assert.DoesNotContain(Password, ex.ToString(), StringComparison.Ordinal);

        // Still refused on the next call, without the old pool quietly standing in for it.
        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => CurrentUserAsync(connections));

        // Fixing the secret restores service.
        wanted = good;
        Assert.NotEmpty(await CurrentUserAsync(connections));
    }

    [Fact]
    public async Task A_rotated_login_that_cannot_connect_is_refused()
    {
        string good = await LoginAsync("good");
        string wanted = good;
        using RotatingWarehouseConnections connections = ConnectionsFor(good, () => wanted);
        _ = await CurrentUserAsync(connections);

        wanted = new NpgsqlConnectionStringBuilder(good) { Password = "wrong-" + Password, Timeout = 3 }.ConnectionString;

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => CurrentUserAsync(connections));
    }
}
