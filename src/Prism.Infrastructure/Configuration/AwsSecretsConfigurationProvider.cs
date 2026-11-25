using Microsoft.Extensions.Configuration;

namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Owns the loader's configuration layer. A refresh replaces the complete immutable-by-convention dictionary
/// before it signals configuration reload, so consumers never observe a partially updated secret set.
/// </summary>
internal sealed class AwsSecretsConfigurationProvider : ConfigurationProvider
{
    private IReadOnlyDictionary<string, string?> _values = new Dictionary<string, string?>();

    internal IReadOnlyDictionary<string, string?> Values => _values;

    internal void NotifyReload() => OnReload();

    internal void Replace(IReadOnlyDictionary<string, string?> values, bool signalReload)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        _values = copy;
        Data = copy;
        if (signalReload)
            OnReload();
    }
}

internal sealed class AwsSecretsConfigurationSource(AwsSecretsConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
