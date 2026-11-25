using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Tests.Configuration;

// Mutates process-wide PRISM_SECRET_OVERRIDE__* variables; serialize with the other loader tests that do.
[Collection(nameof(SecretsLoaderEnvironment))]
public sealed class AwsSecretsLoaderExtensionsTests
{
    private static AwsSecretsLoaderOptions Options(params AwsSecretsLoaderEntry[] entries) => new()
    {
        Enabled = true,
        Secrets = [.. entries],
    };

    [Fact]
    public void AddAwsSecretsLoaderAsync_NullConfiguration_ThrowsSynchronously()
    {
        var environment = new TestHostEnvironment("Testing");

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = AwsSecretsLoaderExtensions.AddAwsSecretsLoaderAsync(null!, environment, cancellationToken: TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public void BindOptions_KeysWithConfigurationDelimiters_PreservesCompleteDestinationKeys()
    {
        using var configuration = new ConfigurationManager();
        using var json = new MemoryStream(
            """
            {
              "Aws": {
                "SecretsLoader": {
                  "Enabled": true,
                  "Secrets": [
                    {
                      "SecretId": "nemsis/dev/php-forward-test-soap",
                      "Keys": {
                        "PhpForwardTest:Forward:HmacSecret": "hmac_secret",
                        "PhpForwardTest:Forward:QueueUrl": "queue_url"
                      }
                    }
                  ]
                }
              }
            }
            """u8.ToArray());
        configuration.AddJsonStream(json);

        AwsSecretsLoaderOptions options = AwsSecretsLoaderExtensions.BindOptions(configuration);

        AwsSecretsLoaderEntry entry = Assert.Single(options.Secrets);
        Assert.Equal("hmac_secret", entry.Keys["PhpForwardTest:Forward:HmacSecret"]);
        Assert.Equal("queue_url", entry.Keys["PhpForwardTest:Forward:QueueUrl"]);
    }

    [Fact]
    public void AddAwsSecretsLoaderCoreAsync_NullOptions_ThrowsSynchronously()
    {
        using var configuration = new ConfigurationManager();
        var reader = new FakeAwsSecretStringReader();

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, null!, reader, NullLogger.Instance, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_Disabled_ReturnsEmpty_AndNeverCallsReader()
    {
        var options = new AwsSecretsLoaderOptions { Enabled = false };
        var reader = new FakeAwsSecretStringReader();
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Same(AwsSecretsLoaderResult.Empty, result);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_EnabledWithNoSecrets_ReturnsEmpty_AndWarns()
    {
        var options = new AwsSecretsLoaderOptions { Enabled = true, Secrets = [] };
        var reader = new FakeAwsSecretStringReader();
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.Empty(result.LoadedKeys);
        Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_RequiredEntry_FetchFailure_Throws()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "nemsis/stage/db", Format = AwsSecretFormat.RdsPostgres, Optional = false };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new AmazonSecretsManagerException("boom") { ErrorCode = "InternalServiceErrorException" });
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderException ex = await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.IsType<AmazonSecretsManagerException>(ex.InnerException);
        Assert.Contains("'nemsis/stage/db'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("InternalServiceErrorException", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("boom", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AccessDeniedException")]
    [InlineData("DecryptionFailure")]
    [InlineData("ThrottlingException")]
    public async Task AddAwsSecretsLoaderCoreAsync_RequiredEntry_FetchFailure_LogsCriticalNamingSecretAndErrorCode(string errorCode)
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/prod/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        // The SDK message stands in for text that may carry an endpoint or credential and must never be logged.
        reader.SetException(entry.SecretId, new AmazonSecretsManagerException("sdk-detail-must-not-leak") { ErrorCode = errorCode });
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, logger, TestContext.Current.CancellationToken));

        CapturedLogMessage failure = Assert.Single(logger.Messages, m => m.Level == LogLevel.Critical);
        Assert.Contains("AWS Secrets Manager loader: FAILED at startup", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'prism/prod/database'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(errorCode, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("sdk-detail-must-not-leak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_NonAwsFetchFailure_NamesSecretAndExceptionType()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/prod/redis", AllowedKeys = ["Demo:SharedRedisConnectionString"] };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new HttpRequestException("connection refused to host-must-not-leak"));
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        AwsSecretsLoaderException ex = await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, logger, TestContext.Current.CancellationToken));

        Assert.Contains("'prism/prod/redis'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(HttpRequestException), ex.Message, StringComparison.Ordinal);
        Assert.Single(logger.Messages, m => m.Level == LogLevel.Critical && m.Message.Contains("FAILED at startup", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("host-must-not-leak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_ConfigurationFailure_LogsCriticalBeforeAnyFetch()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/prod/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        options.ReloadIntervalSeconds = 0;
        var reader = new FakeAwsSecretStringReader();
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, logger, TestContext.Current.CancellationToken));

        CapturedLogMessage failure = Assert.Single(logger.Messages, m => m.Level == LogLevel.Critical);
        Assert.Contains("FAILED at startup", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ReloadIntervalSeconds", failure.Message, StringComparison.Ordinal);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_MissingPropertyFailure_LogsCriticalOnceWithoutValues()
    {
        AwsSecretsLoaderEntry entry = new()
        {
            SecretId = "prism/prod/keys",
            Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["EmSmart2:PrivateKeyPem"] = "private" },
        };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"public":"public-value-must-not-leak"}""");
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, logger, TestContext.Current.CancellationToken));

        CapturedLogMessage failure = Assert.Single(logger.Messages, m => m.Level == LogLevel.Critical);
        Assert.Contains("'prism/prod/keys'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'private'", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("public-value-must-not-leak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OptionalEntry_ResourceNotFound_IsSkipped()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "nemsis/dev/example-service", Optional = true, KeyPrefix = "Example:Service", AllowedKeys = ["Example:Service:Host"] };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new ResourceNotFoundException("not found"));
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Contains(entry.SecretId, result.SkippedSecretIds);
        Assert.Empty(result.LoadedSecretIds);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OptionalEntry_AccessDeniedByErrorCode_IsSkipped()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/dev/keys", Optional = true, Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["Demo:EmSmart2:JwtPrivateKeyPem"] = "private" } };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new AmazonSecretsManagerException("denied") { ErrorCode = "AccessDeniedException" });
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Contains(entry.SecretId, result.SkippedSecretIds);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OptionalEntry_OtherAmazonError_StillThrows()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/dev/keys", Optional = true, Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["Demo:EmSmart2:JwtPrivateKeyPem"] = "private" } };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new AmazonSecretsManagerException("weird") { ErrorCode = "SomeOtherError" });
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderException ex = await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.IsType<AmazonSecretsManagerException>(ex.InnerException);
        Assert.Contains("SomeOtherError", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OptionalEntry_NullSecretString_StillThrows()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "nemsis/stage/rotated", Optional = true, TargetKey = "Some:Key" };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, null);
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderException ex = await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.Contains("AWSCURRENT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_TargetKeyAlreadySet_SkipsWithoutCallingReader()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres, TargetKey = "ConnectionStrings:Writer" };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Writer"] = "Host=already-set;" });

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Contains(entry.SecretId, result.SkippedSecretIds);
        Assert.Empty(reader.Calls);
        Assert.Equal("Host=already-set;", configuration["ConnectionStrings:Writer"]);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_LoadsExpectedKeys_ForRdsPostgresEntry()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""");
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Contains("ConnectionStrings:Writer", result.LoadedKeys);
        Assert.Contains(entry.SecretId, result.LoadedSecretIds);
        Assert.Contains("Host=db.example.com", configuration["ConnectionStrings:Writer"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_PrecedenceEnvVarAlreadySet_LoaderDoesNotOverride()
    {
        // Design section: appending AddInMemoryCollection after the env-var source would let the loader win
        // outright; the entry-level skip-if-already-set rule exists specifically to preserve the pre-loader
        // behaviour where an env-injected value wins. See AwsDatabaseSecretMergeGate.ShouldMerge.
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""");
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Writer"] = "Host=env-var-value;" });

        await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal("Host=env-var-value;", configuration["ConnectionStrings:Writer"]);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OnlyUseAwsSecrets_EnvVarAlreadySet_LoaderFetchesAndOverrides()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        options.OnlyUseAwsSecrets = true;
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""");
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Writer"] = "Host=env-var-value;" });
        var logger = new CapturingLogger();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.Single(reader.Calls);
        Assert.Contains(entry.SecretId, result.LoadedSecretIds);
        Assert.DoesNotContain(entry.SecretId, result.SkippedSecretIds);
        Assert.Contains("Host=db.example.com", configuration["ConnectionStrings:Writer"], StringComparison.OrdinalIgnoreCase);
        CapturedLogMessage replaced = Assert.Single(logger.Messages, m => m.Message.Contains("replaced a different pre-existing value", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings:Writer", replaced.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("env-var-value", StringComparison.Ordinal) || m.Message.Contains("db.example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OnlyUseAwsSecrets_SameValueAlreadySet_DoesNotReportReplacement()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/redis", AllowedKeys = ["Demo:SharedRedisConnectionString"] };
        var options = Options(entry);
        options.OnlyUseAwsSecrets = true;
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"Demo:SharedRedisConnectionString":"redis:6379"}""");
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:SharedRedisConnectionString"] = "redis:6379" });
        var logger = new CapturingLogger();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.Contains(entry.SecretId, result.LoadedSecretIds);
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("replaced a different pre-existing value", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OnlyUseAwsSecrets_KeyMissingFromAwsPayload_WarnsItStillComesFromElsewhere()
    {
        AwsSecretsLoaderEntry entry = new()
        {
            SecretId = "prism/stage/redis",
            AllowedKeys = ["Demo:SharedRedisConnectionString", "Demo:SubmitDataRateLimitRedisConnectionString"],
        };
        var options = Options(entry);
        options.OnlyUseAwsSecrets = true;
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"Demo:SharedRedisConnectionString":"redis-from-aws:6379"}""");
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:SharedRedisConnectionString"] = "redis-from-helm:6379",
            ["Demo:SubmitDataRateLimitRedisConnectionString"] = "ratelimit-from-helm:6379",
        });
        var logger = new CapturingLogger();

        await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.Equal("redis-from-aws:6379", configuration["Demo:SharedRedisConnectionString"]);
        CapturedLogMessage warning = Assert.Single(logger.Messages, m => m.Level == LogLevel.Warning);
        Assert.Contains("Demo:SubmitDataRateLimitRedisConnectionString", warning.Message, StringComparison.Ordinal);
        Assert.Contains(entry.SecretId, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Demo:SharedRedisConnectionString", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains("-from-helm", StringComparison.Ordinal) || m.Message.Contains("-from-aws", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_OnlyUseAwsSecrets_OptionalSecretMissing_WarnsKeysStillComeFromElsewhere()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "nemsis/stage/php-forward-test-database", Optional = true, AllowedKeys = ["ConnectionStrings:PhpForwardTestDb"] };
        var options = Options(entry);
        options.OnlyUseAwsSecrets = true;
        var reader = new FakeAwsSecretStringReader();
        reader.SetException(entry.SecretId, new ResourceNotFoundException("not found"));
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PhpForwardTestDb"] = "Host=from-helm;" });
        var logger = new CapturingLogger();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.Contains(entry.SecretId, result.SkippedSecretIds);
        CapturedLogMessage warning = Assert.Single(logger.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("OnlyUseAwsSecrets", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings:PhpForwardTestDb", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_PrecedenceSidecarAddedAfterLoader_SidecarWins()
    {
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""");
        using var configuration = new ConfigurationManager();

        await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        // Simulates MergeSidecarAppSettingsIfEnabledAsync, which is appended after the loader.
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Writer"] = "Host=sidecar-value;" });

        Assert.Equal("Host=sidecar-value;", configuration["ConnectionStrings:Writer"]);
    }

    [Fact]
    public async Task AddAwsSecretsLoaderAsync_BreakGlassOverride_BeatsLoader_AndIsFoldedIntoLoadedKeysForRedaction()
    {
        const string envVarName = "PRISM_SECRET_OVERRIDE__ConnectionStrings__Writer";
        const string overrideValue = "Host=break-glass;";
        Environment.SetEnvironmentVariable(envVarName, overrideValue);
        try
        {
            AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.RdsPostgres };
            var options = Options(entry);
            var reader = new FakeAwsSecretStringReader();
            reader.SetSecretString(entry.SecretId, """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""");
            using var configuration = new ConfigurationManager();

            AwsSecretsLoaderResult loaded = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);
            AwsSecretsLoaderResult result = configuration.ApplySecretOverridesFromEnvironment(loaded);

            Assert.Equal(overrideValue, configuration["ConnectionStrings:Writer"]);
            Assert.Contains("ConnectionStrings:Writer", result.LoadedKeys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task AddAwsSecretsLoaderCoreAsync_NeverLogsAnyLoadedValue()
    {
        const string secretValue = "super-secret-connection-string-value";
        AwsSecretsLoaderEntry entry = new() { SecretId = "prism/stage/database", Format = AwsSecretFormat.Raw, TargetKey = "Demo:RawSecret", Optional = false };
        var options = Options(entry);
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString(entry.SecretId, secretValue);
        using var configuration = new ConfigurationManager();
        var logger = new CapturingLogger();

        await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.Messages, m => m.Message.Contains(secretValue, StringComparison.Ordinal));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Prism.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record CapturedLogMessage(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
    {
        public List<CapturedLogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(new CapturedLogMessage(logLevel, formatter(state, exception)));
    }
}
