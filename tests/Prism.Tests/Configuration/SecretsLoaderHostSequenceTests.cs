using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Tests.Configuration;

/// <summary>
/// Exercises the whole host startup configuration sequence rather than the loader in isolation.
/// </summary>
/// <remarks>
/// Two defects got through unit testing because every existing test drove the loader on its own. The loader
/// was internally consistent; what was wrong was its position relative to the other merges. Break-glass
/// overrides were applied inside the loader and were therefore beaten by the App Config merge that every
/// host runs afterwards — the opposite of the documented guarantee. And the activation flag is read before
/// that same merge, so App Config cannot switch the loader on at all.
///
/// These tests reproduce the real order from the three hosts:
///   appsettings -> env vars -> AddAwsSecretsLoaderAsync -> MergeSidecarAppSettingsIfEnabledAsync
///   -> ApplySecretOverridesFromEnvironment
/// The sidecar merge is stood in for by an appended <c>AddInMemoryCollection</c>, which is exactly what
/// <c>FeatureManagementConfigurationBootstrap</c> does with the values it fetches.
/// </remarks>
// Mutates process-wide PRISM_SECRET_OVERRIDE__* variables; serialize with the other loader tests that do.
[Collection(nameof(SecretsLoaderEnvironment))]
public sealed class SecretsLoaderHostSequenceTests
{
    private const string WriterKey = "ConnectionStrings:Writer";
    private const string RdsSecretJson =
        """{"hostname":"loader.example.com","database":"nemsis","username":"u","password":"p"}""";

    [Fact]
    public async Task AppConfigBeatsTheLoader_ForAnOverlappingKey()
    {
        using var configuration = BaselineConfiguration();
        var reader = Reader();

        AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, LoaderOptions(), reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        MergeSidecarAppSettings(configuration, "Host=appconfig;");
        configuration.ApplySecretOverridesFromEnvironment(loaded);

        // Documented precedence: freeform tuning still wins over a loaded secret.
        Assert.Equal("Host=appconfig;", configuration[WriterKey]);
    }

    [Fact]
    public async Task BreakGlassBeatsAppConfig_WhichIsTheWholePointOfTheChannel()
    {
        const string envVarName = "PRISM_SECRET_OVERRIDE__ConnectionStrings__Writer";
        Environment.SetEnvironmentVariable(envVarName, "Host=break-glass;");
        try
        {
            using var configuration = BaselineConfiguration();
            var reader = Reader();

            AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, LoaderOptions(), reader, NullLogger.Instance, TestContext.Current.CancellationToken);

            MergeSidecarAppSettings(configuration, "Host=appconfig;");
            AwsSecretsLoaderResult final = configuration.ApplySecretOverridesFromEnvironment(loaded);

            // The incident this channel exists for: a bad App Config value is in place, the operator sets an
            // override and restarts. Before the fix the App Config value still won.
            Assert.Equal("Host=break-glass;", configuration[WriterKey]);

            // And the override's value must still be redacted from the startup configuration dump.
            Assert.Contains(WriterKey, final.LoadedKeys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task LoaderBeatsAnEnvironmentVariable_ButOnlyWhenTheKeyWasNotAlreadySet()
    {
        // Skip-if-already-set: an env-injected writer still wins, which is what makes the chart cutover safe.
        using var configuration = BaselineConfiguration();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [WriterKey] = "Host=env-var;" });

        AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, LoaderOptions(), Reader(), NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal("Host=env-var;", configuration[WriterKey]);
        Assert.DoesNotContain(WriterKey, loaded.LoadedKeys);
    }

    [Fact]
    public async Task AppConfigCannotEnableTheLoader_BecauseItIsMergedAfterTheActivationDecision()
    {
        // Pins the real constraint behind the documented rollout. The activation lever is the chart
        // environment variable Aws__SecretsLoader__Enabled, NOT an App Config freeform key: by the time the
        // sidecar merge runs, the loader has already read Enabled and returned. If this test ever starts
        // failing, someone has made App Config activation work and the runbook must change with it.
        using var configuration = new ConfigurationManager();
        var reader = Reader();
        var options = LoaderOptions();
        options.Enabled = false;

        AwsSecretsLoaderResult first = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        // App Config arrives afterwards and says "enabled" — too late, and it cannot replay the decision.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Aws:SecretsLoader:Enabled"] = "true",
        });

        Assert.Same(AwsSecretsLoaderResult.Empty, first);
        Assert.Empty(reader.Calls);
        Assert.Null(configuration[WriterKey]);
        Assert.Equal("true", configuration["Aws:SecretsLoader:Enabled"]);
    }

    private static ConfigurationManager BaselineConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:SomeUnrelatedSetting"] = "from-appsettings",
        });
        return configuration;
    }

    private static AwsSecretsLoaderOptions LoaderOptions() => new()
    {
        Enabled = true,
        Region = "us-east-1",
        Secrets =
        [
            new AwsSecretsLoaderEntry
            {
                SecretId = "prism/stage/database",
                Format = AwsSecretFormat.RdsPostgres,
                TargetKey = WriterKey,
            },
        ],
    };

    private static FakeAwsSecretStringReader Reader()
    {
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("prism/stage/database", RdsSecretJson);
        return reader;
    }

    /// <summary>Stands in for <c>MergeSidecarAppSettingsIfEnabledAsync</c>, which appends an in-memory source.</summary>
    private static void MergeSidecarAppSettings(ConfigurationManager configuration, string writerValue) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [WriterKey] = writerValue });
}
