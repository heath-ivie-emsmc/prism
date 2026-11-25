using Microsoft.Extensions.Logging;

namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Secrets loader log messages. They carry secret ids, configuration key names, counts and AWS error codes only: never a secret value,
/// SDK message text or an exception object. Every failure line contains the marker <c>AWS Secrets Manager loader: FAILED</c>, so one query finds them all.
/// </summary>
internal static partial class SecretsLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Critical, Message = "AWS Secrets Manager loader: FAILED at startup; the host will not start. {Reason}")]
    public static partial void StartupFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "AWS Secrets Manager loader: dropping property '{SourceProperty}' from secret '{SecretId}' because produced key '{ProducedKey}' is not in AllowedKeys.")]
    public static partial void PropertyDropped(ILogger logger, string sourceProperty, string secretId, string producedKey);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: skipping secret '{SecretId}' at startup because every configuration key it would set is already configured; it stays in the hourly refresh plan and its AWS value takes precedence from the first refresh that differs.")]
    public static partial void SkippedAlreadyConfigured(ILogger logger, string secretId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "Aws:SecretsLoader is enabled but Aws:SecretsLoader:Secrets is empty; no secrets were loaded.")]
    public static partial void NoEntries(ILogger logger);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "AWS Secrets Manager loader: skipping optional secret '{SecretId}' ({Reason}).")]
    public static partial void OptionalSkipped(ILogger logger, string secretId, string reason);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: OnlyUseAwsSecrets replaced a different pre-existing value with the AWS value from secret '{SecretId}' for key(s) [{ReplacedKeys}].")]
    public static partial void OnlyAwsReplaced(ILogger logger, string secretId, string replacedKeys);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "AWS Secrets Manager loader: OnlyUseAwsSecrets is on, but key(s) [{NotFromAwsKeys}] that secret '{SecretId}' should supply did not come from AWS; they still resolve from another configuration source, such as an environment variable.")]
    public static partial void OnlyAwsNotFromAws(ILogger logger, string notFromAwsKeys, string secretId);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: loaded {LoadedCount} secret(s) [{LoadedSecretIds}], skipped {SkippedCount} [{SkippedSecretIds}], setting {KeyCount} configuration key(s) [{LoadedKeys}].")]
    public static partial void Loaded(ILogger logger, int loadedCount, string loadedSecretIds, int skippedCount, string skippedSecretIds, int keyCount, string loadedKeys);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Warning, Message = "AWS Secrets Manager loader: {OverrideCount} break-glass override(s) applied from {Prefix}* environment variable(s): {OverriddenKeys}. These outrank both the loaded secret and App Config.")]
    public static partial void BreakGlassApplied(ILogger logger, int overrideCount, string prefix, string overriddenKeys);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: hourly refresh is active with a {ReloadIntervalSeconds}-second interval.")]
    public static partial void RefreshActive(ILogger logger, int reloadIntervalSeconds);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug, Message = "AWS Secrets Manager loader: hourly refresh found no secret changes.")]
    public static partial void RefreshUnchanged(ILogger logger);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Error, Message = "AWS Secrets Manager loader: FAILED hourly refresh; the complete last-known-good secret set remains active. {Reason}")]
    public static partial void RefreshFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Error, Message = "AWS Secrets Manager loader: FAILED hourly refresh ({FailureType}); the complete last-known-good secret set remains active.")]
    public static partial void RefreshFailedUnexpectedly(ILogger logger, string failureType);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: hourly refresh loaded secret '{SecretId}', which was skipped at startup because its keys were already configured; the AWS value now takes precedence over the pre-existing configuration value.")]
    public static partial void RefreshSupersededStartupValue(ILogger logger, string secretId);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Error, Message = "AWS Secrets Manager loader: FAILED reload subscriber after a published secret refresh ({FailureType}). Configuration was not rolled back; affected consumers may need attention.")]
    public static partial void ReloadSubscriberFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Information, Message = "AWS Secrets Manager loader: hourly refresh applied {SecretCount} secret(s) and {KeyCount} configuration key(s) atomically.")]
    public static partial void RefreshApplied(ILogger logger, int secretCount, int keyCount);
}
