using Testcontainers.Redis;

namespace Prism.Tests.Integration.Persistence;

/// <summary>A throwaway Redis for the replay store.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public ValueTask InitializeAsync() => new(container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
