namespace Prism.Infrastructure.FeatureManagement;

/// <summary>
/// An App Config freeform profile tried to set a configuration key that may not change at runtime. It is an <see cref="InvalidOperationException"/>
/// so bootstrap treats it as a configuration defect and does not retry it. The message names the key (configuration, not a secret), capped in length.
/// </summary>
public sealed class SidecarSettingRefusedException : InvalidOperationException
{
    private const int MaxKeyLength = 100;

    public SidecarSettingRefusedException(string key, string deniedPrefix)
        : base($"App Config appsettings key '{Describe(key)}' is not allowed to be set at runtime (denylisted: '{deniedPrefix}').")
    {
    }

    private static string Describe(string key) => key.Length <= MaxKeyLength ? key : key[..MaxKeyLength] + "...";
}
