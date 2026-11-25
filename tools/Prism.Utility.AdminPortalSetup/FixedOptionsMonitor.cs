using Microsoft.Extensions.Options;

namespace Prism.Utility.AdminPortalSetup;

/// <summary>A one-shot tool has options that never change, but the app-token factory reads them through a monitor.</summary>
internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    where T : class
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
