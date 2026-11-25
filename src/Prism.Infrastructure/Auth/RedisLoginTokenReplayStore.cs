using System.Security.Cryptography;
using System.Text;
using Prism.Application.Auth;
using Prism.Infrastructure.Configuration;
using StackExchange.Redis;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Replay store shared by every instance: one atomic <c>SET NX</c> with an expiry per login token id. If Redis cannot be reached
/// the call throws and the sign-in fails, rather than letting a token through unchecked. The connection string is read on every call:
/// when a rotated secret changes it, new calls use a new connection and calls already running finish on the old one, which is then closed.
/// A failed connection never falls back to memory.
/// </summary>
public sealed class RedisLoginTokenReplayStore : ILoginTokenReplayStore, IDisposable
{
    public const string KeyPrefix = "prism:login-token:";

    private readonly ReloadingResource<ConnectionMultiplexer> connections;

    public RedisLoginTokenReplayStore(string connectionString)
        : this(Fixed(connectionString))
    {
    }

    public RedisLoginTokenReplayStore(Func<string> currentConnectionString)
    {
        ArgumentNullException.ThrowIfNull(currentConnectionString);

        connections = new ReloadingResource<ConnectionMultiplexer>(currentConnectionString, Connect);
        // Connect now so a bad connection string is reported at start, as before. The lease is released at once: the connection stays current.
        connections.Acquire().Dispose();
    }

    public async ValueTask<bool> TryRememberAsync(string tokenId, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        cancellationToken.ThrowIfCancellationRequested();

        // The id comes from a token header chosen by the portal. Hashing keeps the key a fixed size and free of odd characters.
        string key = KeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId)));
        using ReloadingResource<ConnectionMultiplexer>.Lease lease = connections.Acquire();
        return await lease.Resource.GetDatabase().StringSetAsync(key, "1", window, When.NotExists);
    }

    public void Dispose() => connections.Dispose();

    private static Func<string> Fixed(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return () => connectionString;
    }

    private static ConnectionMultiplexer Connect(string connectionString)
    {
        // abortConnect=false: the host starts even if Redis is briefly down, and each call then fails closed on its own.
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 3000;
        options.SyncTimeout = 3000;
        options.AsyncTimeout = 3000;
        return ConnectionMultiplexer.Connect(options);
    }
}
