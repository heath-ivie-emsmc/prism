
namespace Prism.Application.FeatureManagement;

public static class FeatureValueManagerExtensions
{
    public static Task<bool> IsEnabledForProviderAsync(
        this IFeatureValueManager manager,
        string featureName,
        long providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);

        return manager.IsEnabledAsync(
            featureName,
            new FeatureContext { ProviderId = providerId },
            cancellationToken);
    }

    public static Task<bool> IsEnabledForUserAsync(
        this IFeatureValueManager manager,
        string featureName,
        long userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);

        return manager.IsEnabledAsync(
            featureName,
            new FeatureContext { UserId = userId },
            cancellationToken);
    }

    public static Task<bool> IsEnabledAsync(
        this IFeatureValueManager manager,
        string featureName,
        long providerId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);

        return manager.IsEnabledAsync(
            featureName,
            new FeatureContext
            {
                ProviderId = providerId,
                UserId = userId
            },
            cancellationToken);
    }
}
