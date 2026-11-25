using Microsoft.Extensions.Configuration;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class SidecarConfigurationProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    public void Replace(IReadOnlyDictionary<string, string?> data)
    {
        Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}
