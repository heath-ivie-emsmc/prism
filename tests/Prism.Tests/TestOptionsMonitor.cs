using Microsoft.Extensions.Options;

namespace Prism.Tests;

/// <summary>An options monitor whose value a test can replace, to prove a consumer reads the current value on every call.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    where T : class
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
