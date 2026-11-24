using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Api;
using Prism.Infrastructure.Warehouse;
using Prism.Testing.Common;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>
/// The startup write protection check against a real PostgreSQL in a throwaway container. Roles are created here to stand in for warehouse logins:
/// nothing in this class can reach a real warehouse, because every connection comes from <see cref="PostgresFixture"/> through <see cref="TestDatabaseGuard"/>.
/// </summary>
public sealed class WarehouseWriteProtectionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = "NeverShowThis-Pw1";
    private static int roleCounter;

    private static string NewRole(string kind) => $"wh_{kind}_{Interlocked.Increment(ref roleCounter)}";

    private async Task AdminAsync(string sql)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Warehouse-shaped schemas and a few tables, created by the container's own superuser.</summary>
    private Task SeedWarehouseShapeAsync() => AdminAsync(
        """
        CREATE SCHEMA IF NOT EXISTS reporting;
        CREATE SCHEMA IF NOT EXISTS ods;
        CREATE TABLE IF NOT EXISTS reporting.trip_detail_snapshot (provider_id int NOT NULL, encounter_id bigint NOT NULL, origin_address text);
        CREATE TABLE IF NOT EXISTS reporting.kpi_net_collections_ap__prov (provider_id int NOT NULL, net_collections numeric);
        CREATE TABLE IF NOT EXISTS ods.encounter_transaction (id bigint PRIMARY KEY, encounter_id bigint NOT NULL);
        CREATE OR REPLACE VIEW reporting.trip_view AS SELECT provider_id, encounter_id FROM reporting.trip_detail_snapshot;
        CREATE SEQUENCE IF NOT EXISTS ods.encounter_seq;
        """);

    /// <summary>A login that can connect and read both schemas, and nothing else. Extra SQL adds the one thing a test wants to prove is refused.</summary>
    private async Task<string> ReadOnlyLoginAsync(string kind, string extraAfterRole = "", string attributes = "")
    {
        await SeedWarehouseShapeAsync();
        string role = NewRole(kind);
        await AdminAsync(
            $"""
            CREATE ROLE {role} LOGIN PASSWORD '{Password}' {attributes};
            GRANT CONNECT ON DATABASE {new NpgsqlConnectionStringBuilder(postgres.ConnectionString).Database} TO {role};
            GRANT USAGE ON SCHEMA reporting, ods TO {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA reporting, ods TO {role};
            {extraAfterRole.Replace("{role}", role, StringComparison.Ordinal)}
            """);
        return role;
    }

    private string ConnectionStringFor(string role)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Username = role, Password = Password };
        return TestDatabaseGuard.Assert(builder.ConnectionString);
    }

    private static WarehouseWriteProtectionCheck CheckFor(string connectionString, int timeoutSeconds = 30)
    {
        var options = new PrismWarehouseOptions { ConnectionString = connectionString, CommandTimeoutSeconds = timeoutSeconds };
        return new WarehouseWriteProtectionCheck(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<WarehouseWriteProtectionCheck>.Instance);
    }

    private async Task<string> RefusalAsync(string role)
    {
        WarehouseWriteProtectionException ex = await Assert.ThrowsAsync<WarehouseWriteProtectionException>(() => CheckFor(ConnectionStringFor(role)).VerifyAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("Refusing to start:", ex.Message, StringComparison.Ordinal);
        Assert.Contains(role, ex.Message, StringComparison.Ordinal);
        AssertNoSecrets(ex.Message);
        return ex.Message;
    }

    private void AssertNoSecrets(string text)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(builder.Password ?? "never", text.Replace(builder.Username ?? "", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_container_login_which_is_a_superuser_is_refused()
    {
        await SeedWarehouseShapeAsync();
        var superuser = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);

        WarehouseWriteProtectionException ex = await Assert.ThrowsAsync<WarehouseWriteProtectionException>(() => CheckFor(TestDatabaseGuard.Assert(superuser.ConnectionString)).VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("is not read-only", ex.Message, StringComparison.Ordinal);
        Assert.Contains("it is a superuser", ex.Message, StringComparison.Ordinal);
        Assert.Contains("it can create objects in the database", ex.Message, StringComparison.Ordinal);
        Assert.Contains("table(s)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("docs/warehouse.md", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_with_select_only_on_reporting_and_ods_passes_and_reports_its_session_default()
    {
        string role = await ReadOnlyLoginAsync("ro");
        await AdminAsync($"ALTER ROLE {role} SET default_transaction_read_only = on");

        WarehouseWriteProtectionReport report = await CheckFor(ConnectionStringFor(role)).InspectAsync(TestContext.Current.CancellationToken);
        await CheckFor(ConnectionStringFor(role)).VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Problems);
        Assert.True(report.IsReadOnly);
        Assert.Equal(role, report.RoleName);
        Assert.True(report.SessionReadOnly);
    }

    [Fact]
    public async Task A_select_only_login_passes_even_when_temporary_tables_are_allowed_because_they_never_touch_the_warehouse()
    {
        string role = await ReadOnlyLoginAsync("temp");

        WarehouseWriteProtectionReport report = await CheckFor(ConnectionStringFor(role)).InspectAsync(TestContext.Current.CancellationToken);

        Assert.True(report.IsReadOnly);
        Assert.True(report.TemporaryTablesAllowed);
    }

    [Fact]
    public async Task A_login_with_insert_on_one_table_is_refused()
    {
        string role = await ReadOnlyLoginAsync("ins", "GRANT INSERT ON reporting.trip_detail_snapshot TO {role};");

        string message = await RefusalAsync(role);

        Assert.Contains("can write to", message, StringComparison.Ordinal);
        Assert.Contains("1 table(s) or view(s)", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("TRUNCATE")]
    [InlineData("REFERENCES")]
    [InlineData("TRIGGER")]
    public async Task Any_one_write_privilege_on_a_table_is_refused(string privilege)
    {
        string role = await ReadOnlyLoginAsync("priv", $"GRANT {privilege} ON ods.encounter_transaction TO {{role}};");

        Assert.Contains("1 table(s) or view(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_write_privilege_on_a_view_or_a_single_column_is_refused()
    {
        string onView = await ReadOnlyLoginAsync("view", "GRANT UPDATE ON reporting.trip_view TO {role};");
        string onColumn = await ReadOnlyLoginAsync("col", "GRANT UPDATE (origin_address) ON reporting.trip_detail_snapshot TO {role};");

        Assert.Contains("can write to", await RefusalAsync(onView), StringComparison.Ordinal);
        Assert.Contains("can write to", await RefusalAsync(onColumn), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_can_use_a_sequence_is_refused()
    {
        string role = await ReadOnlyLoginAsync("seq", "GRANT USAGE ON SEQUENCE ods.encounter_seq TO {role};");

        Assert.Contains("1 sequence(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_can_create_in_a_schema_is_refused()
    {
        string role = await ReadOnlyLoginAsync("cre", "GRANT CREATE ON SCHEMA reporting TO {role};");

        Assert.Contains("can create objects in 1 schema(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_can_create_in_the_database_is_refused()
    {
        string database = new NpgsqlConnectionStringBuilder(postgres.ConnectionString).Database!;
        string role = await ReadOnlyLoginAsync("dbcre", $"GRANT CREATE ON DATABASE {database} TO {{role}};");

        Assert.Contains("can create objects in the database", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_owns_a_table_is_refused()
    {
        await SeedWarehouseShapeAsync();
        string role = await ReadOnlyLoginAsync("own");
        await AdminAsync($"CREATE TABLE IF NOT EXISTS reporting.owned_by_{role} (id int); ALTER TABLE reporting.owned_by_{role} OWNER TO {role};");

        string message = await RefusalAsync(role);

        Assert.Contains("owns 1 table(s), view(s) or sequence(s)", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_owns_a_schema_is_refused()
    {
        string role = await ReadOnlyLoginAsync("ownsch");
        await AdminAsync($"CREATE SCHEMA own_{role} AUTHORIZATION {role}");

        Assert.Contains("owns 1 schema(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_is_a_member_of_a_role_with_write_access_is_refused()
    {
        await SeedWarehouseShapeAsync();
        string writer = NewRole("writer");
        await AdminAsync($"CREATE ROLE {writer} NOLOGIN; GRANT USAGE ON SCHEMA reporting TO {writer}; GRANT INSERT, UPDATE ON reporting.trip_detail_snapshot TO {writer};");
        string role = await ReadOnlyLoginAsync("member", $"GRANT {writer} TO {{role}};");

        string message = await RefusalAsync(role);

        Assert.Contains("can write to", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_is_a_member_of_a_privileged_role_is_refused_even_when_it_does_not_inherit_its_privileges()
    {
        string role = await ReadOnlyLoginAsync("pgw", "GRANT pg_write_all_data TO {role} WITH INHERIT FALSE;");

        Assert.Contains("member of 1 privileged role(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CREATEDB", "it can create databases")]
    [InlineData("CREATEROLE", "it can create roles")]
    [InlineData("REPLICATION", "it has the replication privilege")]
    [InlineData("BYPASSRLS", "it bypasses row level security")]
    public async Task A_login_with_an_administrative_attribute_is_refused(string attribute, string expected)
    {
        string role = await ReadOnlyLoginAsync("attr", attributes: attribute);

        Assert.Contains(expected, await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_is_a_member_of_a_superuser_role_is_refused()
    {
        await SeedWarehouseShapeAsync();
        string admin = NewRole("adminrole");
        await AdminAsync($"CREATE ROLE {admin} NOLOGIN SUPERUSER");
        string role = await ReadOnlyLoginAsync("insuper", $"GRANT {admin} TO {{role}};");

        Assert.Contains("privileged role(s)", await RefusalAsync(role), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_warehouse_that_cannot_be_reached_refuses_to_start_without_echoing_the_connection()
    {
        string unreachable = $"Host=127.0.0.1;Port=1;Database=dw;Username=prism_ro;Password={Password};Timeout=2";

        WarehouseWriteProtectionException ex = await Assert.ThrowsAsync<WarehouseWriteProtectionException>(() => CheckFor(unreachable, timeoutSeconds: 2).VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("could not be checked", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("prism_ro", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_check_reads_the_catalog_in_a_read_only_transaction_and_changes_nothing()
    {
        string role = await ReadOnlyLoginAsync("noop");
        string before = await CatalogFingerprintAsync();

        await CheckFor(ConnectionStringFor(role)).VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(before, await CatalogFingerprintAsync());
    }

    private async Task<string> CatalogFingerprintAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT (SELECT count(*) FROM pg_class) || '/' || (SELECT count(*) FROM pg_roles) || '/' || (SELECT count(*) FROM pg_namespace)", admin);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    // Host level: the check runs as a hosted service, in every environment, before the host serves anything.

    private static async Task<WarehouseWriteProtectionException> StartRefusedAsync(string environment, string warehouse)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Prism"] = "Host=unused.invalid;Database=unused",
            ["ConnectionStrings:Warehouse"] = warehouse,
            ["Warehouse:SkipLoginCheck"] = "false", // appsettings.Development.json may turn the check off for a developer; this test needs it on
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddPrismPersistence(builder.Configuration);
        await using WebApplication app = builder.Build();

        return await Assert.ThrowsAsync<WarehouseWriteProtectionException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task A_host_in_a_local_environment_refuses_to_start_with_a_login_that_can_write(string environment)
    {
        await SeedWarehouseShapeAsync();

        WarehouseWriteProtectionException ex = await StartRefusedAsync(environment, postgres.ConnectionString);

        Assert.Contains("it is a superuser", ex.Message, StringComparison.Ordinal);
        AssertNoSecrets(ex.Message);
    }

    [Fact]
    public async Task A_host_starts_with_a_read_only_login_and_logs_that_it_verified_it()
    {
        string role = await ReadOnlyLoginAsync("host");
        using var factory = new AuthTestFactory(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Warehouse"] = ConnectionStringFor(role) },
            verifyWarehouseWriteProtection: true);

        using HttpClient warm = factory.CreateClient();

        Assert.IsType<NpgsqlWarehouse>(factory.Services.GetRequiredService<Prism.Application.Warehouse.IWarehouse>());
        Assert.Contains(factory.Logs.Entries, entry => entry.Level == LogLevel.Information && entry.Text.Contains(role, StringComparison.Ordinal) && entry.Text.Contains("verified read-only", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_test_host_with_a_login_that_can_write_does_not_start()
    {
        await SeedWarehouseShapeAsync();
        using var factory = new AuthTestFactory(
            settings: new Dictionary<string, string?> { ["ConnectionStrings:Warehouse"] = postgres.ConnectionString },
            verifyWarehouseWriteProtection: true);

        Exception ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.NotNull(Find<WarehouseWriteProtectionException>(ex));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_temporary_override_lets_a_test_host_start_with_a_login_that_can_write_and_logs_an_error()
    {
        await SeedWarehouseShapeAsync();
        using var factory = new AuthTestFactory(
            settings: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Warehouse"] = postgres.ConnectionString,
                ["Warehouse:SkipLoginCheck"] = "true",
            },
            verifyWarehouseWriteProtection: true);

        using HttpClient warm = factory.CreateClient();

        Assert.Contains(factory.Logs.Entries, entry => entry.Level == LogLevel.Error && entry.Text.Contains("TEMPORARY OVERRIDE", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public void A_deployed_host_rejects_the_override()
    {
        IReadOnlyList<string> problems = PrismWarehouseOptionsRules.Problems(
            new PrismWarehouseOptions { ConnectionString = "Host=x;SSL Mode=Require", SkipLoginCheck = true }, deployed: true);

        Assert.Contains(problems, p => p.Contains("SkipLoginCheck", StringComparison.Ordinal));
        Assert.DoesNotContain(PrismWarehouseOptionsRules.Problems(new PrismWarehouseOptions { SkipLoginCheck = true }, deployed: false), p => p.Contains("SkipLoginCheck", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_warehouse_configured_the_check_is_skipped_the_warning_is_logged_and_the_host_starts()
    {
        using var factory = new AuthTestFactory(verifyWarehouseWriteProtection: true);

        using HttpClient warm = factory.CreateClient();

        Assert.IsType<NotConfiguredWarehouseWriteProtectionCheck>(factory.Services.GetRequiredService<IWarehouseWriteProtectionCheck>());
        Assert.IsType<UnconfiguredWarehouse>(factory.Services.GetRequiredService<Prism.Application.Warehouse.IWarehouse>());
        Assert.Contains(factory.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Text.Contains("No warehouse connection is configured", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Fact]
    public void The_real_check_and_its_startup_are_part_of_the_host_composition()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Prism"] = "Host=unused.invalid;Database=unused",
            ["ConnectionStrings:Warehouse"] = "Host=dw.invalid;Database=dw;Username=u;Password=p",
        }).Build();
        services.AddPrismPersistence(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(WarehouseWriteProtectionStartup));
        Assert.Contains(services, d => d.ServiceType == typeof(IWarehouseWriteProtectionCheck));
    }

    private static T? Find<T>(Exception ex)
        where T : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is T found)
            {
                return found;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.Select(Find<T>).FirstOrDefault(x => x is not null) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
