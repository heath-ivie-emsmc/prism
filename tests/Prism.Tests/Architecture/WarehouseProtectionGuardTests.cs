using System.Text.RegularExpressions;
using Prism.Testing.Common;

namespace Prism.Tests.Architecture;

/// <summary>
/// The warehouse is read-only: no schema change, migration, write or DDL against it from any code, test or tool. These guards fail the build when code or a test could
/// reach the warehouse another way than <c>IWarehouse</c>, or when anything in the repository would change it. AGENTS.md, "The warehouse is read-only".
/// </summary>
public sealed partial class WarehouseProtectionGuardTests
{
    private static readonly string[] RepoRoots = ["src", "tools", "tests"];
    private static readonly string[] SqlRoots = ["src", "tools", "tests", ".github", "scripts", "docker", "web"];
    private static readonly string[] NonProductionRoots = ["tests", "tools"];

    // Layer 4(b): the connection-building calls a test may use only on a container database it created.
    private static readonly string[] ConnectionTokens = ["new NpgsqlConnection(", "NpgsqlDataSource.Create", "NpgsqlDataSourceBuilder", "UseNpgsql(", "UsePrismNpgsql("];

    private static readonly string[] WarehouseConnectionNames =
    [
        "ConnectionStrings:Warehouse", "ConnectionStrings__Warehouse", "GetConnectionString(\"Warehouse\")", "PrismWarehouseOptions.ConnectionStringName", "Warehouse__ConnectionString",
    ];

    /// <summary>Test files that may name the warehouse connection setting, each with why. Every one uses a fake string (dw.invalid) or a container database through the guard.</summary>
    private static readonly Dictionary<string, string> TestsMayNameWarehouseConnection = new(StringComparer.Ordinal)
    {
        ["WarehouseWiringTests.cs"] = "wiring test with a fake dw.invalid connection",
        ["SecureCookieAndConfigurationTests.cs"] = "configuration validation with fake dw.invalid connections",
        ["WarehouseWriteProtectionTests.cs"] = "uses only the container database through TestDatabaseGuard",
        ["AuthTestFactory.cs"] = "blanks the setting so an environment variable can never reach a test host",
        ["WarehouseProtectionGuardTests.cs"] = "this guard",
    };

    private static readonly string[] ConfigurationSources = ["AddUserSecrets", "AddEnvironmentVariables(", "UserSecrets"];

    /// <summary>Builds an EF model against a fake string and never opens a connection.</summary>
    private static readonly string[] ModelOnlyFiles = ["EmsmcModelConventionsTests.cs"];

    private static readonly string[] AllowedTestEnvironmentVariables = ["PRISM_UPDATE_SNAPSHOTS"];

    private static IEnumerable<string> AllFiles(string folder, string pattern = "*.cs") => RepoFiles.Source(folder, pattern);

    private static void AssertNone(IEnumerable<string> offenders)
    {
        List<string> list = [.. offenders];
        Assert.True(list.Count == 0, string.Join(Environment.NewLine, list));
    }

    private static string StatementFrom(string text, int index)
    {
        int end = text.IndexOf(';', index);
        return end < 0 ? text[index..] : text[index..end];
    }

    // ---- Layer 4(a), test side: TestDatabaseGuard itself.

    [Fact]
    public void Only_the_staff_provider_list_may_run_a_warehouse_query_that_names_no_provider()
    {
        // A query across Providers is the one exception to "every query filters on @provider_id". It must stay a single named place.
        List<string> users = [.. AllFiles("src")
            .Where(file => File.ReadAllText(file).Contains("AcrossProviders = true", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .Select(path => path.Replace('\\', '/'))];

        Assert.Equal(["src/Prism.Infrastructure/Warehouse/WarehouseProviderList.cs"], users);
    }

    [Fact]
    public void The_test_database_guard_accepts_only_a_registered_local_container_database()
    {
        const string container = "Host=localhost;Port=55432;Database=guard_unit;Username=u;Password=p";
        TestDatabaseGuard.Register(container);

        Assert.Equal(container, TestDatabaseGuard.Assert(container));
        // Same container, different credentials: still the same database.
        Assert.NotNull(TestDatabaseGuard.Assert("Host=localhost;Port=55432;Database=guard_unit;Username=other;Password=x"));
    }

    [Theory]
    [InlineData("Host=dw.example.com;Port=5432;Database=guard_unit;Username=u;Password=SecretPw1")]
    [InlineData("Host=dw.prod.internal.example;Database=reporting;Username=u;Password=SecretPw1")]
    [InlineData("Host=localhost;Port=55432;Database=reporting;Username=u;Password=SecretPw1")]
    [InlineData("Host=localhost;Port=55433;Database=guard_unit;Username=u;Password=SecretPw1")]
    [InlineData("Host=8.8.8.8;Port=55432;Database=guard_unit;Username=u;Password=SecretPw1")]
    [InlineData("Host=localhost.evil.example;Port=55432;Database=guard_unit;Username=u;Password=SecretPw1")]
    [InlineData("Host=localhost,dw.example.com;Port=55432;Database=guard_unit;Username=u;Password=SecretPw1")]
    [InlineData("Username=u;Password=SecretPw1")]
    [InlineData("")]
    public void The_test_database_guard_refuses_anything_else_without_echoing_the_connection(string connectionString)
    {
        TestDatabaseGuard.Register("Host=localhost;Port=55432;Database=guard_unit");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.Assert(connectionString));

        Assert.DoesNotContain("SecretPw1", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Host=dw.example.com;Port=5432;Database=x")]
    [InlineData("Host=1.2.3.4;Port=5432;Database=x")]
    public void A_fixture_cannot_register_a_remote_database_as_a_test_database(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.Register(connectionString));

    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=x")]
    [InlineData("Host=10.1.2.3;Port=5432;Database=x2")]
    [InlineData("Host=172.17.0.2;Port=5432;Database=x3")]
    [InlineData("Host=192.168.1.5;Port=5432;Database=x4")]
    [InlineData("Host=host.docker.internal;Port=5432;Database=x5")]
    public void A_local_or_private_network_container_host_can_be_registered(string connectionString) =>
        Assert.Equal(connectionString, TestDatabaseGuard.Register(connectionString));

    // ---- Layer 4(a)/(b): every connection a test opens comes from a container.

    [Fact]
    public void Tests_open_database_connections_only_with_a_connection_string_that_passed_the_test_database_guard()
    {
        // The connection-building calls are allowed in a test only when the same statement asserts the guard. PostgresFixture is the one place a container is
        // started and a context built from its own string.
        var offenders = new List<string>();
        foreach (string file in AllFiles("tests").Where(f => !f.EndsWith("GuardTests.cs", StringComparison.Ordinal) && !f.EndsWith("PostgresFixture.cs", StringComparison.Ordinal) && !ModelOnlyFiles.Contains(Path.GetFileName(f))))
        {
            string text = File.ReadAllText(file);
            foreach (string token in ConnectionTokens)
            {
                for (int index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
                {
                    if (!StatementFrom(text, index).Contains("TestDatabaseGuard.Assert(", StringComparison.Ordinal))
                    {
                        offenders.Add($"{RepoFiles.Relative(file)}: {token} without TestDatabaseGuard.Assert");
                    }
                }
            }
        }

        AssertNone(offenders);
    }

    [Fact]
    public void Only_the_postgres_fixture_starts_a_database_container_and_registers_it_with_the_guard()
    {
        var offenders = AllFiles("tests")
            .Where(f => !f.EndsWith("PostgresFixture.cs", StringComparison.Ordinal) && !f.EndsWith("GuardTests.cs", StringComparison.Ordinal) && !f.EndsWith("TestDatabaseGuard.cs", StringComparison.Ordinal))
            .Where(f =>
            {
                string text = File.ReadAllText(f);
                return text.Contains("PostgreSqlBuilder", StringComparison.Ordinal) || text.Contains("TestDatabaseGuard.Register(", StringComparison.Ordinal);
            })
            .Select(RepoFiles.Relative)
            .ToList();

        AssertNone(offenders);
        string fixture = File.ReadAllText(AllFiles("tests").Single(f => f.EndsWith("PostgresFixture.cs", StringComparison.Ordinal)));
        Assert.Contains("TestDatabaseGuard.Register(", fixture, StringComparison.Ordinal);
        Assert.Contains("TestDatabaseGuard.Assert(container.GetConnectionString())", fixture, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_integration_helper_that_creates_or_drops_tables_asserts_the_test_database_guard_first()
    {
        // A file that issues DDL or a DROP in a command must open its connection through the guard (the connection token check above already forces it per statement).
        var offenders = new List<string>();
        foreach (string file in AllFiles("tests/Prism.Tests.Integration").Where(f => !f.EndsWith("PostgresFixture.cs", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(file);
            bool executes = text.Contains("ExecuteNonQueryAsync", StringComparison.Ordinal) || text.Contains("ExecuteNonQuery(", StringComparison.Ordinal);
            if (executes && DdlOrDropInTest().IsMatch(text) && !text.Contains("TestDatabaseGuard.Assert(", StringComparison.Ordinal))
            {
                offenders.Add(RepoFiles.Relative(file));
            }
        }

        AssertNone(offenders);
    }

    [Fact]
    public void Tests_and_tools_never_read_the_warehouse_connection_from_real_configuration_user_secrets_or_the_environment()
    {
        var offenders = new List<string>();
        foreach (string file in NonProductionRoots.SelectMany(root => AllFiles(root)))
        {
            string name = Path.GetFileName(file);
            string text = File.ReadAllText(file);
            bool mentions = WarehouseConnectionNames.Any(n => text.Contains(n, StringComparison.Ordinal));
            if (mentions && (!OwnsTestsFolder(file) || !TestsMayNameWarehouseConnection.ContainsKey(name)))
            {
                offenders.Add($"{RepoFiles.Relative(file)}: names the warehouse connection setting");
            }

            if (OwnsTestsFolder(file) && TestsMayNameWarehouseConnection.ContainsKey(name) && mentions && !name.Equals("WarehouseProtectionGuardTests.cs", StringComparison.Ordinal))
            {
                bool fake = text.Contains("dw.invalid", StringComparison.Ordinal) || text.Contains("TestDatabaseGuard.Assert(", StringComparison.Ordinal) || name == "AuthTestFactory.cs";
                if (!fake)
                {
                    offenders.Add($"{RepoFiles.Relative(file)}: allowed to name the warehouse setting, but uses neither a dw.invalid string nor a guarded container string");
                }
            }

            if (!name.Equals("WarehouseProtectionGuardTests.cs", StringComparison.Ordinal) && !name.Equals("GuardTests.cs", StringComparison.Ordinal))
            {
                offenders.AddRange(ConfigurationSourceCalls(text).Select(call => $"{RepoFiles.Relative(file)}: {call}"));
                offenders.AddRange(EnvironmentVariableNames(text, file).Select(call => $"{RepoFiles.Relative(file)}: reads environment variable {call}"));
            }
        }

        AssertNone(offenders);
    }

    [Fact]
    public void The_test_host_blanks_the_warehouse_connection_so_an_environment_variable_never_reaches_it()
    {
        string factory = File.ReadAllText(AllFiles("tests/Prism.Tests.Integration").Single(f => f.EndsWith("AuthTestFactory.cs", StringComparison.Ordinal)));

        Assert.Contains("[\"ConnectionStrings:Warehouse\"] = \"\"", factory, StringComparison.Ordinal);
    }

    private static bool OwnsTestsFolder(string file) => RepoFiles.Relative(file).StartsWith("tests", StringComparison.Ordinal);

    private static IEnumerable<string> ConfigurationSourceCalls(string text) =>
        ConfigurationSources.Where(call => text.Contains(call, StringComparison.Ordinal));

    private static IEnumerable<string> EnvironmentVariableNames(string text, string file)
    {
        foreach (Match call in EnvironmentVariableRead().Matches(text))
        {
            string name = call.Groups["name"].Success ? call.Groups["name"].Value : "(not a literal)";
            bool tool = RepoFiles.Relative(file).StartsWith("tools", StringComparison.Ordinal);
            bool allowed = AllowedTestEnvironmentVariables.Contains(name, StringComparer.Ordinal) || (tool && name is "PRISM_PORTAL_KEY_FILE");
            if (!allowed)
            {
                yield return name;
            }
        }
    }

    // ---- Layer 4(c): production code reaches the warehouse only through the warehouse folder and its one registration point.

    [Fact]
    public void Production_code_outside_the_warehouse_folder_and_its_registration_never_touches_the_warehouse_connection()
    {
        string[] banned = [.. WarehouseConnectionNames, "PrismWarehouseOptions", "WarehouseDataSourceFactory"];
        var offenders = new List<string>();
        foreach (string file in AllFiles("src").Where(f => !IsWarehouseFolderOrRegistration(f) && !IsOwnDatabaseConnectionCode(f)))
        {
            string text = File.ReadAllText(file);
            offenders.AddRange(banned.Where(token => text.Contains(token, StringComparison.Ordinal)).Select(token => $"{RepoFiles.Relative(file)}: {token}"));
        }

        AssertNone(offenders);
    }

    private static bool IsWarehouseFolderOrRegistration(string file)
    {
        string relative = RepoFiles.Relative(file).Replace('\\', '/');
        return relative.StartsWith("src/Prism.Infrastructure/Warehouse/", StringComparison.Ordinal) || relative == "src/Prism.Api/PersistenceServiceCollectionExtensions.cs";
    }

    // Prism's own database options carry their own ConnectionStringName constant. They are not the warehouse's.
    private static bool IsOwnDatabaseConnectionCode(string file) =>
        RepoFiles.Relative(file).Replace('\\', '/').Contains("/Persistence/PrismDatabaseOptions", StringComparison.Ordinal) && !File.ReadAllText(file).Contains("Warehouse", StringComparison.Ordinal);

    [Fact]
    public void No_entity_framework_context_is_configured_with_the_warehouse_and_the_only_context_is_prisms_own()
    {
        var offenders = new List<string>();
        foreach (string file in AllFiles("src"))
        {
            string text = File.ReadAllText(file);
            foreach (Match use in DbContextConfiguration().Matches(text))
            {
                string statement = StatementFrom(text, use.Index);
                if (statement.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{RepoFiles.Relative(file)}: {use.Value} is configured with the warehouse");
                }
            }

            foreach (Match context in DbContextSubclass().Matches(text))
            {
                if (context.Groups["name"].Value != "PrismDbContext")
                {
                    offenders.Add($"{RepoFiles.Relative(file)}: {context.Groups["name"].Value} is a DbContext other than PrismDbContext");
                }
            }
        }

        AssertNone(offenders);
    }

    [Fact]
    public void Tools_scripts_docker_and_ci_never_reference_the_warehouse_connection()
    {
        string[] banned = [.. WarehouseConnectionNames, "PrismWarehouseOptions", "WarehouseDataSourceFactory", "Prism.Infrastructure.Warehouse", "ConnectionStrings__Warehouse"];
        var offenders = new List<string>();
        foreach (string folder in new[] { "tools", "scripts", "docker", ".github", ".vscode" })
        {
            if (!Directory.Exists(Path.Combine(RepoFiles.Root, folder)))
            {
                continue;
            }

            foreach (string file in AllFiles(folder, "*").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                string text = File.ReadAllText(file);
                offenders.AddRange(banned.Where(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)).Select(token => $"{RepoFiles.Relative(file)}: {token}"));
            }
        }

        AssertNone(offenders);
    }

    [Fact]
    public void The_warehouse_has_no_migrations_and_nothing_in_the_repository_targets_its_schemas_with_sql()
    {
        List<string> offenders = [.. ForeignMigrationFolders(), .. PersistenceNamingWarehouseSchemas(), .. MigrationBuilderOutsidePersistence(), .. SqlFiles()];

        AssertNone(offenders);
    }

    private static bool InOwnPersistence(string file) =>
        RepoFiles.Relative(file).Replace('\\', '/').StartsWith("src/Prism.Infrastructure/Persistence/", StringComparison.Ordinal);

    // Migrations exist for Prism's own database only, in one folder.
    private static IEnumerable<string> ForeignMigrationFolders() =>
        RepoRoots
            .SelectMany(folder => Directory.EnumerateDirectories(Path.Combine(RepoFiles.Root, folder), "Migrations", SearchOption.AllDirectories))
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(d => RepoFiles.Relative(d).Replace('\\', '/') != "src/Prism.Infrastructure/Persistence/Migrations")
            .Select(d => $"{RepoFiles.Relative(d)}: a Migrations folder that is not Prism's own database");

    // Prism's own persistence code (context, configuration, every migration) says nothing about the warehouse schemas.
    private static IEnumerable<string> PersistenceNamingWarehouseSchemas() =>
        AllFiles("src")
            .Where(InOwnPersistence)
            .Where(file => WarehouseSchemaName().IsMatch(File.ReadAllText(file)))
            .Select(file => $"{RepoFiles.Relative(file)}: Prism's own persistence code names a warehouse schema (reporting or ods)");

    private static IEnumerable<string> MigrationBuilderOutsidePersistence() =>
        AllFiles("src").Concat(AllFiles("tools")).Concat(AllFiles("tests"))
            .Where(file => !InOwnPersistence(file) && !file.EndsWith("WarehouseProtectionGuardTests.cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("migrationBuilder", StringComparison.Ordinal))
            .Select(file => $"{RepoFiles.Relative(file)}: migrationBuilder outside Prism's own persistence");

    private static IEnumerable<string> SqlFiles() =>
        SqlRoots
            .Where(folder => Directory.Exists(Path.Combine(RepoFiles.Root, folder)))
            .SelectMany(folder => AllFiles(folder, "*.sql"))
            .Select(file => $"{RepoFiles.Relative(file)}: a .sql file. The warehouse is changed by the database team, never from this repository");

    // ---- Layer 4(d): no production SQL that writes to or changes the warehouse schemas.

    [Fact]
    public void Production_sql_never_contains_ddl_or_dml_against_the_reporting_or_ods_schemas()
    {
        var offenders = AllFiles("src")
            .Where(f => !InOwnPersistence(f))
            .SelectMany(file => WarehouseWriteSql().Matches(File.ReadAllText(file)).Select(match => $"{RepoFiles.Relative(file)}: {match.Value}"))
            .ToList();

        AssertNone(offenders);
    }

    [Theory]
    [InlineData("INSERT INTO reporting.trip VALUES (1)")]
    [InlineData("insert  into\n ods.credit (id) VALUES (1)")]
    [InlineData("UPDATE ods.credit SET id = 1")]
    [InlineData("DELETE FROM \"reporting\".trip")]
    [InlineData("TRUNCATE TABLE reporting.trip")]
    [InlineData("TRUNCATE ods.credit")]
    [InlineData("DROP TABLE IF EXISTS ods.credit")]
    [InlineData("ALTER TABLE reporting.trip ADD COLUMN x int")]
    [InlineData("CREATE TABLE ods.x (a int)")]
    [InlineData("CREATE OR REPLACE VIEW reporting.v AS SELECT 1")]
    [InlineData("CREATE SCHEMA ods")]
    [InlineData("GRANT SELECT ON ods.credit TO u")]
    [InlineData("REFRESH MATERIALIZED VIEW reporting.mv")]
    [InlineData("COPY reporting.trip FROM STDIN")]
    [InlineData("MERGE INTO ods.credit USING x ON true")]
    public void The_sql_scan_recognises_ddl_and_dml_against_the_warehouse_schemas(string sql) =>
        Assert.Matches(WarehouseWriteSql(), sql);

    [Theory]
    [InlineData("SELECT * FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id")]
    [InlineData("SELECT x FROM ods.credit c JOIN ods.encounter_transaction t ON t.id = c.id")]
    [InlineData("a code comment saying reporting is read-only")]
    public void The_sql_scan_leaves_reads_alone(string sql) =>
        Assert.DoesNotMatch(WarehouseWriteSql(), sql);

    [Fact]
    public void The_scans_in_this_class_find_the_things_they_look_for()
    {
        // So a guard cannot pass by finding nothing: the repository does contain these forms.
        Assert.Contains(AllFiles("src"), f => File.ReadAllText(f).Contains("reporting.", StringComparison.Ordinal));
        Assert.Contains(AllFiles("src"), f => DbContextConfiguration().IsMatch(File.ReadAllText(f)));
        Assert.Contains(AllFiles("src"), f => DbContextSubclass().IsMatch(File.ReadAllText(f)));
        Assert.Contains(AllFiles("tests"), f => ConnectionTokens.Any(t => File.ReadAllText(f).Contains(t, StringComparison.Ordinal)));
        Assert.Contains(AllFiles("tests"), f => EnvironmentVariableRead().IsMatch(File.ReadAllText(f)));
        Assert.Contains(AllFiles("tests/Prism.Tests.Integration"), f => DdlOrDropInTest().IsMatch(File.ReadAllText(f)));
        Assert.Matches(WarehouseSchemaName(), "migrationBuilder.Sql(\"create table reporting.x\")");
        Assert.Matches(DbContextConfiguration(), "services.AddDbContext<X>(o => o.UseNpgsql(cs))");
    }

    [GeneratedRegex(@"\b(CREATE\s+(SCHEMA|TABLE)|DROP\s+TABLE|INSERT\s+INTO)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DdlOrDropInTest();

    [GeneratedRegex(@"GetEnvironmentVariable\(\s*(""(?<name>[^""]*)"")?", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EnvironmentVariableRead();

    [GeneratedRegex(@"\b(UseNpgsql|UsePrismNpgsql|AddDbContext|AddDbContextFactory|AddDbContextPool)\b", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DbContextConfiguration();

    [GeneratedRegex(@"class\s+(?<name>\w+)\s*(\([^)]*\))?\s*:\s*DbContext\b", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DbContextSubclass();

    [GeneratedRegex(@"\b(reporting|ods)\s*\.\s*[A-Za-z_""]|[""'](reporting|ods)[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WarehouseSchemaName();

    [GeneratedRegex(
        @"\b(insert\s+into|update|delete\s+from|merge\s+into|truncate(\s+table)?|drop\s+(table|view|schema|materialized\s+view|index)(\s+if\s+exists)?|alter\s+(table|schema|view|materialized\s+view)|create\s+(or\s+replace\s+)?(unique\s+)?(table|view|schema|materialized\s+view|index|function)(\s+if\s+not\s+exists)?|copy|refresh\s+materialized\s+view(\s+concurrently)?|grant\s+\w+(\s*,\s*\w+)*\s+on(\s+(table|schema|all\s+tables\s+in\s+schema))?|revoke\s+\w+(\s*,\s*\w+)*\s+on(\s+(table|schema))?)\s+(only\s+)?""?(reporting|ods)\b",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WarehouseWriteSql();
}
