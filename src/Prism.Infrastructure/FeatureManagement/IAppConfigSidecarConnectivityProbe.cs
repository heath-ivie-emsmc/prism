
using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public interface IAppConfigSidecarConnectivityProbe
{
    Task<bool> IsReachableAsync(FeatureManagementOptions options, CancellationToken cancellationToken);
}
