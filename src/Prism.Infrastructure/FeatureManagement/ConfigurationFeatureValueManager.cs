
using Microsoft.Extensions.Configuration;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class ConfigurationFeatureValueManager : IFeatureValueManager
{
    public const string SectionName = "FeatureFlags";

    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> EmptySettings { get; } =
        new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public ConfigurationFeatureValueManager(IConfiguration configuration, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public Task<FeatureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, FeatureValue> features = _configuration
            .GetSection(SectionName)
            .GetChildren()
            .Where(section => !string.IsNullOrWhiteSpace(section.Key))
            .ToDictionary(
                section => section.Key,
                section => new FeatureValue
                {
                    FeatureName = section.Key,
                    Enabled = section.Get<bool>(),
                    Variant = null,
                    Settings = EmptySettings,
                },
                StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new FeatureSnapshot
        {
            RetrievedAt = _timeProvider.GetUtcNow(),
            Features = features,
        });
    }

    public Task<FeatureValue?> GetFeatureValueAsync(
        string featureName,
        CancellationToken cancellationToken = default) =>
        GetFeatureValueAsync(featureName, FeatureContext.Empty, cancellationToken);

    public Task<FeatureValue?> GetFeatureValueAsync(
        string featureName,
        FeatureContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        ArgumentNullException.ThrowIfNull(context);

        IConfigurationSection section = _configuration.GetSection(SectionName).GetSection(featureName);
        if (!section.Exists())
        {
            return Task.FromResult<FeatureValue?>(null);
        }

        bool enabled = section.Get<bool>();
        return Task.FromResult<FeatureValue?>(new FeatureValue
        {
            FeatureName = featureName,
            Enabled = enabled,
            Variant = null,
            Settings = EmptySettings
        });
    }

    public async Task<bool> IsEnabledAsync(
        string featureName,
        CancellationToken cancellationToken = default)
    {
        FeatureValue? featureValue = await GetFeatureValueAsync(
                featureName,
                FeatureContext.Empty,
                cancellationToken)
            .ConfigureAwait(false);

        return featureValue?.Enabled ?? false;
    }

    public async Task<bool> IsEnabledAsync(
        string featureName,
        FeatureContext context,
        CancellationToken cancellationToken = default)
    {
        FeatureValue? featureValue = await GetFeatureValueAsync(
                featureName,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        return featureValue?.Enabled ?? false;
    }
}
