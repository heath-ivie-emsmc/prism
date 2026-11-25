using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Tests.Configuration;

public sealed class AwsSecretKeyPolicyTests
{
    private static AwsSecretsLoaderEntry Entry(
        List<string>? allowedKeys = null,
        Dictionary<string, string>? keys = null,
        string? targetKey = null,
        AwsSecretFormat format = AwsSecretFormat.Json) => new()
        {
            SecretId = "test/secret",
            Format = format,
            TargetKey = targetKey,
            Keys = keys ?? [],
            AllowedKeys = allowedKeys ?? [],
        };

    [Theory]
    [InlineData("environment")]
    [InlineData("Environment")]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("DOTNET_ENVIRONMENT")]
    [InlineData("Aws:SecretsLoader:Enabled")]
    [InlineData("FeatureManagement:Enabled")]
    [InlineData("FeatureFlags:SomeFlag")]
    [InlineData("Serilog:MinimumLevel")]
    [InlineData("ForwardedHeaders:KnownProxies:0")]
    [InlineData("Internal:GroupName")]
    [InlineData("Warehouse:SkipLoginCheck")]
    [InlineData("AllowedHosts")]
    public void ApplyAndFilter_ProducedKeyMatchingDenylist_Throws(string producedKey)
    {
        AwsSecretsLoaderEntry entry = Entry(targetKey: producedKey);
        var properties = new[] { new AwsSecretFlattenedProperty("SecretString", producedKey, "value") };

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretKeyPolicy.ApplyAndFilter(entry, properties, NullLogger.Instance));
    }

    [Fact]
    public void ApplyAndFilter_ProducedKeyContainingDoubleUnderscore_Throws()
    {
        AwsSecretsLoaderEntry entry = Entry(targetKey: "ConnectionStrings__Reporting");
        var properties = new[] { new AwsSecretFlattenedProperty("SecretString", "ConnectionStrings__Reporting", "value") };

        AwsSecretsLoaderException ex = Assert.Throws<AwsSecretsLoaderException>(
            () => AwsSecretKeyPolicy.ApplyAndFilter(entry, properties, NullLogger.Instance));
        Assert.Contains("__", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAndFilter_AllowedKeys_DropsUnlistedProducedKey_AndKeepsListed()
    {
        AwsSecretsLoaderEntry entry = Entry(allowedKeys:
        [
            "Example:Service:ApiKey",
            "Example:Service:Host",
        ]);
        var properties = new[]
        {
            new AwsSecretFlattenedProperty("ApiKey", "Example:Service:ApiKey", "pem-value"),
            new AwsSecretFlattenedProperty("Host", "Example:Service:Host", "service.example.com"),
            new AwsSecretFlattenedProperty("Extra", "Example:Service:Extra", "unexpected"),
        };

        IReadOnlyDictionary<string, string?> result = AwsSecretKeyPolicy.ApplyAndFilter(entry, properties, NullLogger.Instance);

        Assert.Equal(2, result.Count);
        Assert.Equal("pem-value", result["Example:Service:ApiKey"]);
        Assert.Equal("service.example.com", result["Example:Service:Host"]);
        Assert.False(result.ContainsKey("Example:Service:Extra"));
    }

    [Fact]
    public void ApplyAndFilter_DroppedKey_LogsSourcePropertyAndProducedKey_ButNeverTheValue()
    {
        AwsSecretsLoaderEntry entry = Entry(allowedKeys: ["Example:Service:ApiKey"]);
        var properties = new[] { new AwsSecretFlattenedProperty("Extra", "Example:Service:Extra", "super-secret-value") };
        var logger = new CapturingLogger();

        AwsSecretKeyPolicy.ApplyAndFilter(entry, properties, logger);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("Extra", message, StringComparison.Ordinal);
        Assert.Contains("Example:Service:Extra", message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAndFilter_KeyPrefixWithAllowedKeys_OnlyKeepsAllowedProducedKeys()
    {
        AwsSecretsLoaderEntry entry = Entry(allowedKeys:
        [
            "Example:Service:ApiKey",
            "Example:Service:ApiSecret",
            "Example:Service:Host",
            "Example:Service:Port",
            "Example:Service:Username",
        ]);
        var flattened = new[]
        {
            new AwsSecretFlattenedProperty("api_key", "Example:Service:ApiKey", "pem"),
            new AwsSecretFlattenedProperty("host", "Example:Service:Host", "service.example.com"),
            new AwsSecretFlattenedProperty("unexpected_field", "Example:Service:UnexpectedField", "x"),
        };

        IReadOnlyDictionary<string, string?> result = AwsSecretKeyPolicy.ApplyAndFilter(entry, flattened, NullLogger.Instance);

        Assert.Equal(2, result.Count);
        Assert.False(result.ContainsKey("Example:Service:UnexpectedField"));
    }

    [Fact]
    public void ValidateEntryDefinition_NeitherKeysNorTargetKey_RequiresAllowedKeys()
    {
        AwsSecretsLoaderEntry entry = Entry();

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretKeyPolicy.ValidateEntryDefinition(entry));
    }

    [Fact]
    public void ValidateEntryDefinition_BareKeyPrefixAlone_DoesNotSatisfyAllowedKeysRequirement()
    {
        AwsSecretsLoaderEntry entry = Entry();
        entry.KeyPrefix = "Example:Service";

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretKeyPolicy.ValidateEntryDefinition(entry));
    }

    [Fact]
    public void ValidateEntryDefinition_WithAllowedKeys_Passes()
    {
        AwsSecretsLoaderEntry entry = Entry(allowedKeys: ["Some:Key"]);
        entry.KeyPrefix = "Example:Service";

        AwsSecretKeyPolicy.ValidateEntryDefinition(entry);
    }

    [Fact]
    public void ValidateEntryDefinition_WithKeysMapping_DoesNotRequireAllowedKeys()
    {
        AwsSecretsLoaderEntry entry = Entry(keys: new Dictionary<string, string>(StringComparer.Ordinal) { ["Config:Key"] = "prop" });

        AwsSecretKeyPolicy.ValidateEntryDefinition(entry);
    }

    [Fact]
    public void ValidateEntryDefinition_WithTargetKey_DoesNotRequireAllowedKeys()
    {
        AwsSecretsLoaderEntry entry = Entry(targetKey: "ConnectionStrings:Writer");

        AwsSecretKeyPolicy.ValidateEntryDefinition(entry);
    }

    [Fact]
    public void ValidateEntryDefinition_RdsPostgresDefaultTargetKey_DoesNotRequireAllowedKeys()
    {
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.RdsPostgres);

        AwsSecretKeyPolicy.ValidateEntryDefinition(entry);
    }

    [Fact]
    public void ValidateEntryDefinition_RawFormatWithoutTargetKey_Throws()
    {
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.Raw, allowedKeys: ["Some:Key"]);

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretKeyPolicy.ValidateEntryDefinition(entry));
    }

    [Fact]
    public void ValidateEntryDefinition_EmptySecretId_Throws()
    {
        AwsSecretsLoaderEntry entry = Entry(targetKey: "Some:Key");
        entry.SecretId = " ";

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretKeyPolicy.ValidateEntryDefinition(entry));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
