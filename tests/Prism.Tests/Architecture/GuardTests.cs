using System.Text.Json;
using System.Text.RegularExpressions;

namespace Prism.Tests.Architecture;

/// <summary>
/// Rules from AGENTS.md that the compiler cannot enforce. Each fails with the offending file, so a new violation is named in the build output.
/// </summary>
public sealed partial class GuardTests
{
    private static readonly string[] SourceFolders = ["src", "tools", "tests"];
    private static readonly string[] ProductionFolders = ["src", "tools"];
    private static readonly string[] BannedClockAndIdCalls = ["DateTime.UtcNow", "DateTime.Now", "DateTimeOffset.UtcNow", "DateTimeOffset.Now", "Guid.NewGuid()", "new Random("];
    private static readonly string[] DeployedEnvironments = ["Staging", "QA", "Production"];
    private static readonly string[] HeavyTestDependencies = ["Testcontainers", "Mvc.Testing", "Npgsql", "EntityFrameworkCore", "StackExchange.Redis"];
    private static readonly string[] RequiredPortalSettings = ["AccountPortalBaseUrl", "AdminPortalApiBaseUrl", "LoginTokenIssuer", "AdminPortalAudience", "LoginTokenPublicKeyUrl"];

    [Fact]
    public void Every_regex_has_a_match_timeout_or_is_non_backtracking()
    {
        // Rule 4. The usage pattern finds the call or attribute, and the statement up to its end must name a bound.
        var offenders = new List<string>();
        foreach (string file in SourceFolders.SelectMany(folder => RepoFiles.Source(folder)))
        {
            if (file.EndsWith("GuardTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (Match use in RegexUsage().Matches(text))
            {
                // An attribute ends at ")]" (the pattern itself may contain brackets). A call or declaration ends at ";".
                int end = use.Value.StartsWith('[') ? text.IndexOf(")]", use.Index, StringComparison.Ordinal) : text.IndexOf(';', use.Index);
                string statement = end < 0 ? text[use.Index..] : text[use.Index..end];
                if (!statement.Contains("NonBacktracking", StringComparison.Ordinal)
                    && !statement.Contains("matchTimeout", StringComparison.OrdinalIgnoreCase)
                    && !statement.Contains("TimeSpan", StringComparison.Ordinal))
                {
                    offenders.Add($"{RepoFiles.Relative(file)}: {use.Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_guard_can_tell_a_bounded_regex_from_an_unbounded_one()
    {
        // Proves the pattern above sees both forms, so the test above cannot pass by finding nothing.
        Assert.Matches(RegexUsage(), "var r = new Regex(\"x\");");
        Assert.Matches(RegexUsage(), "Regex.IsMatch(a, \"x\")");
        Assert.Matches(RegexUsage(), "[GeneratedRegex(\"x\")]");
        Assert.Contains(
            SourceFolders.SelectMany(folder => RepoFiles.Source(folder)),
            file => File.ReadAllText(file).Contains("GeneratedRegex(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_database_is_only_pointed_at_postgres_through_the_shared_extension()
    {
        var offenders = SourceFolders
            .SelectMany(folder => RepoFiles.Source(folder))
            .Where(file => !file.EndsWith("PrismNpgsqlConfiguration.cs", StringComparison.Ordinal) && !file.EndsWith("GuardTests.cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains(".UseNpgsql(", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_factories_create_database_connections()
    {
        // The warehouse connection gets its read-only, time-limited and encrypted settings in WarehouseDataSourceFactory.
        // Prism's own database goes through PrismNpgsqlConfiguration. Nothing else in production code may open a connection by hand.
        string[] banned = ["new NpgsqlConnection(", "NpgsqlDataSource.Create(", "new NpgsqlDataSourceBuilder("];
        var offenders = ProductionFolders
            .SelectMany(folder => RepoFiles.Source(folder))
            .Where(file => !file.EndsWith("WarehouseDataSourceFactory.cs", StringComparison.Ordinal))
            .SelectMany(file => banned.Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal)).Select(token => $"{RepoFiles.Relative(file)}: {token}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_secrets_loader_talks_to_aws_secrets_manager()
    {
        // Every fetch goes through AwsSecretStringReader: retries, the overall budget and the failure messages that never carry a value live there.
        var offenders = ProductionFolders
            .SelectMany(folder => RepoFiles.Source(folder))
            .Where(file => !file.EndsWith("AwsSecretStringReader.cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("AmazonSecretsManagerClient(", StringComparison.Ordinal))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_loader_and_feature_flags_are_not_registered_from_anywhere_but_the_host_composition()
    {
        // One composition per host, so DI can be tested as a whole: nothing else may start the sidecar or the hourly refresh.
        string[] tokens = ["AddAppConfigSidecar(", "AddAwsSecretsLoaderReloading(", "AddAwsSecretsLoaderAsync("];
        var offenders = ProductionFolders
            .SelectMany(folder => RepoFiles.Source(folder))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Prism.Infrastructure{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.EndsWith("Program.cs", StringComparison.Ordinal)
                        && !file.EndsWith("FeatureManagementServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .SelectMany(file => tokens.Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal)).Select(token => $"{RepoFiles.Relative(file)}: {token}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_and_infrastructure_code_does_not_read_the_system_clock_or_make_random_ids()
    {
        var offenders = ProductionFolders
            .SelectMany(folder => RepoFiles.Source(folder))
            .SelectMany(file => BannedClockAndIdCalls.Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal)).Select(token => $"{RepoFiles.Relative(file)}: {token}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Layers_only_point_inward_and_production_code_never_references_tests()
    {
        Assert.Empty(ProjectReferences("src/Prism.Application/Prism.Application.csproj"));
        Assert.Equal(["Prism.Application"], ProjectReferences("src/Prism.Infrastructure/Prism.Infrastructure.csproj"));
        Assert.DoesNotContain(ProjectReferences("src/Prism.Api/Prism.Api.csproj"), name => name.StartsWith("Prism.Tests", StringComparison.Ordinal) || name == "Prism.Testing.Common");
    }

    [Fact]
    public void The_unit_test_project_references_no_integration_project_and_the_shared_testing_project_stays_free_of_hosts_and_databases()
    {
        Assert.DoesNotContain("Prism.Tests.Integration", ProjectReferences("tests/Prism.Tests/Prism.Tests.csproj"));

        string common = File.ReadAllText(Path.Combine(RepoFiles.Root, "tests/Prism.Testing.Common/Prism.Testing.Common.csproj"));
        foreach (string heavy in HeavyTestDependencies)
        {
            Assert.DoesNotContain(heavy, common, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Deployed_settings_files_use_https_only_and_hold_no_secrets()
    {
        // Rule 7 (no http:// internal URLs outside Development and Testing) and the configuration rule (secrets never in the repo).
        var problems = new List<string>();
        foreach (string file in RepoFiles.Source("src", "appsettings*.json"))
        {
            string name = Path.GetFileName(file);
            bool local = name.Equals("appsettings.Development.json", StringComparison.Ordinal) || name.Equals("appsettings.Testing.json", StringComparison.Ordinal);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(file));
            foreach ((string path, string value) in Strings(json.RootElement, ""))
            {
                if (!local && value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{name}: {path} is an http:// URL");
                }

                if (SecretName().IsMatch(path) && value.Length > 0)
                {
                    problems.Add($"{name}: {path} holds a value, but secrets come from user secrets or Secrets Manager");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Every_deployed_environment_has_a_settings_file_that_names_its_portal_and_leaves_the_application_id_to_be_set()
    {
        foreach (string environment in DeployedEnvironments)
        {
            string path = Path.Combine(RepoFiles.Root, "src/Prism.Api", $"appsettings.{environment}.json");
            Assert.True(File.Exists(path), $"appsettings.{environment}.json is missing");

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement auth = json.RootElement.GetProperty("Auth");
            foreach (string key in RequiredPortalSettings)
            {
                Assert.False(string.IsNullOrWhiteSpace(auth.GetProperty(key).GetString()), $"{environment}: Auth:{key} is empty");
            }

            // The application id and public origin differ per registration and are not known here: unset, so startup validation refuses to run.
            Assert.False(auth.TryGetProperty("ApplicationId", out _), $"{environment}: ApplicationId must be set by whoever registers the app");
        }
    }

    [Fact]
    public void Every_workflow_action_is_pinned_to_a_full_commit_sha()
    {
        var unpinned = new List<string>();
        foreach (string file in RepoFiles.Source(".github/workflows", "*.yml"))
        {
            foreach (Match use in ActionUse().Matches(File.ReadAllText(file)))
            {
                string reference = use.Groups["ref"].Value;
                if (!use.Groups["name"].Value.StartsWith("./", StringComparison.Ordinal) && !FullSha().IsMatch(reference))
                {
                    unpinned.Add($"{RepoFiles.Relative(file)}: {use.Groups["name"].Value}@{reference}");
                }
            }
        }

        Assert.Empty(unpinned);
        Assert.True(RepoFiles.Source(".github/workflows", "*.yml").Any(), "There is no workflow to check.");
    }

    private static IEnumerable<(string Path, string Value)> Strings(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => Strings(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}")),
        JsonValueKind.Array => element.EnumerateArray().SelectMany((item, index) => Strings(item, $"{path}:{index}")),
        JsonValueKind.String => [(path, element.GetString() ?? "")],
        _ => [],
    };

    private static List<string> ProjectReferences(string csproj) =>
        ProjectReferenceInclude().Matches(File.ReadAllText(Path.Combine(RepoFiles.Root, csproj)))
            .Select(match => Path.GetFileNameWithoutExtension(match.Groups[1].Value.Replace('\\', '/')))
            .ToList();

    [GeneratedRegex(@"new\s+Regex\(|Regex\.(IsMatch|Match|Matches|Replace|Split|EnumerateMatches|Count)\(|\[GeneratedRegex\(", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexUsage();

    [GeneratedRegex(@"^\s*-?\s*uses:\s*(?<name>[^@\s]+)@(?<ref>\S+)", RegexOptions.Multiline | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ActionUse();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FullSha();

    [GeneratedRegex(@"ProjectReference\s+Include=""([^""]+)""", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectReferenceInclude();

    [GeneratedRegex("(PrivateKey|Password|SigningKey|Secret|ConnectionString)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretName();
}
