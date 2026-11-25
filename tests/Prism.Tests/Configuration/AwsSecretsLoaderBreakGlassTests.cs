using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Prism.Tests.Configuration;

/// <summary>
/// The break-glass channel exists so an operator can override a bad secret without a deploy. It must not
/// become a way to set a key the loader would refuse to set from a secret payload.
/// </summary>
// Mutates process-wide PRISM_SECRET_OVERRIDE__* variables; serialize with the other loader tests that do.
[Collection(nameof(SecretsLoaderEnvironment))]
public sealed class AwsSecretsLoaderBreakGlassTests
{
    [Theory]
    [InlineData("PRISM_SECRET_OVERRIDE__environment", "Development")]
    [InlineData("PRISM_SECRET_OVERRIDE__Internal__GroupName", "Everyone")]
    [InlineData("PRISM_SECRET_OVERRIDE__FeatureManagement__Enabled", "false")]
    public async Task DeniedKey_IsRejected_EvenThroughTheBreakGlassChannel(string envVarName, string value)
    {
        Environment.SetEnvironmentVariable(envVarName, value);
        try
        {
            var options = new AwsSecretsLoaderOptions { Enabled = true, Secrets = [] };
            using var configuration = new ConfigurationManager();

            AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration,
                options,
                new FakeAwsSecretStringReader(),
                logger: null,
                TestContext.Current.CancellationToken);

            AwsSecretsLoaderException ex = Assert.Throws<AwsSecretsLoaderException>(() =>
                configuration.ApplySecretOverridesFromEnvironment(loaded));

            Assert.Contains("PRISM_SECRET_OVERRIDE__", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task AllowedKey_StillOverridesAndIsRedacted()
    {
        const string envVarName = "PRISM_SECRET_OVERRIDE__ConnectionStrings__Writer";
        Environment.SetEnvironmentVariable(envVarName, "Host=break-glass;");
        try
        {
            var options = new AwsSecretsLoaderOptions { Enabled = true, Secrets = [] };
            using var configuration = new ConfigurationManager();

            AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration,
                options,
                new FakeAwsSecretStringReader(),
                logger: null,
                TestContext.Current.CancellationToken);
            AwsSecretsLoaderResult result = configuration.ApplySecretOverridesFromEnvironment(loaded);

            Assert.Equal("Host=break-glass;", configuration["ConnectionStrings:Writer"]);
            // Folded into LoadedKeys so the override's value is redacted from the startup configuration dump.
            Assert.Contains("ConnectionStrings:Writer", result.LoadedKeys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task PublicEntryPoint_WithNoLoggerArgument_StillRuns()
    {
        // Regression guard for the wiring bug where every host omitted the logger and the loader's entire
        // diagnostic surface — including the startup line the rollout verification depends on — went dead.
        using var configuration = new ConfigurationManager();
        var environment = new BreakGlassHostEnvironment("Production");

        AwsSecretsLoaderResult result = await configuration.AddAwsSecretsLoaderAsync(environment, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(AwsSecretsLoaderResult.Empty, result);
    }

    private sealed class BreakGlassHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Prism.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
