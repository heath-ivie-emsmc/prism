using Prism.Infrastructure.Configuration;

namespace Prism.Tests.Configuration;

/// <summary>Test double for <see cref="IAwsSecretStringReader"/> so loader tests never make an AWS call.</summary>
internal sealed class FakeAwsSecretStringReader : IAwsSecretStringReader
{
    private readonly Dictionary<string, Func<string?>> _responses = [];

    public List<string> Calls { get; } = [];

    public void SetSecretString(string secretId, string? value) => _responses[secretId] = () => value;

    public void SetException(string secretId, Exception exception) => _responses[secretId] = () => throw exception;

    public Task<string?> GetSecretStringAsync(string secretId, CancellationToken cancellationToken)
    {
        Calls.Add(secretId);
        if (!_responses.TryGetValue(secretId, out Func<string?>? factory))
            throw new InvalidOperationException($"FakeAwsSecretStringReader has no response configured for '{secretId}'.");

        return Task.FromResult(factory());
    }
}
