
using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class FeatureValueManager : IFeatureValueManager
{
    private readonly SidecarState _state;

    public FeatureValueManager(SidecarState state) => _state = state;

    public Task<FeatureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SidecarState.Snapshot snapshot = _state.GetSnapshot();

        return Task.FromResult(new FeatureSnapshot
        {
            RetrievedAt = snapshot.RetrievedAt,
            Features = snapshot.Features,
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
        ArgumentNullException.ThrowIfNull(context);

        if (!_state.TryGetFeature(featureName, out FeatureValue? featureValue)
            || featureValue is null)
        {
            return Task.FromResult<FeatureValue?>(null);
        }

        bool evaluatedEnabled = FeatureFlagEvaluation.EvaluateEnabled(featureValue, context);
        return Task.FromResult<FeatureValue?>(featureValue with { Enabled = evaluatedEnabled });
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
