
namespace Prism.Application.FeatureManagement;

public interface IFeatureValueManager
{
    Task<FeatureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<FeatureValue?> GetFeatureValueAsync(string featureName, CancellationToken cancellationToken = default);

    Task<FeatureValue?> GetFeatureValueAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default);
}
