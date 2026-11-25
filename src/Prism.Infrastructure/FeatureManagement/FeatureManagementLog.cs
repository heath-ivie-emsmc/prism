using Microsoft.Extensions.Logging;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>
/// Feature management log messages. Failures carry the exception type only, never the exception object or message, so nothing the sidecar
/// or the network said is copied into a log. Application, environment and profile names are configuration, not secrets.
/// </summary>
internal static partial class FeatureManagementLog
{
    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "AppConfig sidecar is disabled.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Information, Message = "AppConfig sidecar bootstrap succeeded. feature_count={FeatureCount} appsettings_count={AppSettingsCount} base_url={BaseUrl} application={Application} environment={Environment} feature_configuration={FeatureConfiguration}")]
    public static partial void BootstrapSucceeded(ILogger logger, int featureCount, int appSettingsCount, string baseUrl, string application, string environment, string featureConfiguration);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Error, Message = "AppConfig sidecar bootstrap failed ({FailureType}), but StartupFailureMode=FailOpen. Continuing with empty feature state and existing configuration.")]
    public static partial void BootstrapFailedOpen(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Critical, Message = "AppConfig sidecar bootstrap failed ({FailureType}) with StartupFailureMode=FailClosed. Stopping the application.")]
    public static partial void BootstrapFailedClosed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4014, Level = LogLevel.Debug, Message = "AppConfig sidecar bootstrap attempt {Attempt} failed while waiting for the agent to listen. retry_delay_ms={RetryDelayMs}")]
    public static partial void BootstrapWaitingForAgent(ILogger logger, int attempt, long retryDelayMs);

    [LoggerMessage(EventId = 4015, Level = LogLevel.Warning, Message = "AppConfig sidecar bootstrap attempt {Attempt} failed ({FailureType}). retry_delay_ms={RetryDelayMs}")]
    public static partial void BootstrapAttemptFailed(ILogger logger, int attempt, string failureType, long retryDelayMs);

    [LoggerMessage(EventId = 4016, Level = LogLevel.Warning, Message = "AppConfig feature snapshot refresh failed ({FailureType}). Keeping the last known feature state.")]
    public static partial void FeatureRefreshFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4017, Level = LogLevel.Information, Message = "AppConfig freeform appsettings refreshed. appsettings_count={AppSettingsCount}")]
    public static partial void AppSettingsRefreshed(ILogger logger, int appSettingsCount);

    [LoggerMessage(EventId = 4018, Level = LogLevel.Warning, Message = "AppConfig appsettings refresh failed ({FailureType}). Keeping the last known appsettings state.")]
    public static partial void AppSettingsRefreshFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4019, Level = LogLevel.Warning, Message = "AppConfig sidecar appsettings bootstrap failed ({FailureType}) before host build, but StartupFailureMode=FailOpen. Continuing with existing configuration.")]
    public static partial void BootstrapMergeFailedOpen(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4020, Level = LogLevel.Warning, Message = "AppConfig has no '{Configuration}' profile for application '{Application}' environment '{Environment}' (HTTP 404). Continuing with configuration from appsettings only. Create the profile if this environment needs runtime overrides.")]
    public static partial void NoAppSettingsProfile(ILogger logger, string configuration, string application, string environment);
}
