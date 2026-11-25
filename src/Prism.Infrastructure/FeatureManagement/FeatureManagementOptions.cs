using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class FeatureManagementOptions
{
    public const string SectionName = "FeatureManagement";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:2772";

    public string Application { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string FeatureConfiguration { get; set; } = "feature-flags";

    public string AppSettingsConfiguration { get; set; } = "appsettings";

    public bool EnableDynamicAppSettings { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan StartupMaxWait { get; set; } = TimeSpan.FromMinutes(2);

    public bool RequireNonEmptyFeatureConfig { get; set; } = true;

    public StartupFailureMode StartupFailureMode { get; set; } = StartupFailureMode.FailClosed;
}

public enum StartupFailureMode
{
    FailClosed = 0,
    FailOpen = 1
}
