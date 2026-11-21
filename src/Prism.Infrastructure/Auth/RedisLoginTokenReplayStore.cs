using System.Security.Cryptography;
using System.Text;
using Prism.Application.Auth;
using StackExchange.Redis;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Replay store shared by every instance: one atomic <c>SET NX</c> with an expiry per login token id. If Redis cannot be reached
/// the call throws and the sign-in fails, rather than letting a token through unchecked.
/// </summary>
public sealed class RedisLoginTokenReplayStore : ILoginTokenReplayStore, IDisposable
{
    public const string KeyPrefix = "prism:login-token:";

    private readonly ConnectionMultiplexer connection;

    public RedisLoginTokenReplayStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // abortConnect=false: the host starts even if Redis is briefly down, and each call then fails closed on its own.
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 3000;
        options.SyncTimeout = 3000;
        options.AsyncTimeout = 3000;
        connection = ConnectionMultiplexer.Connect(options);
    }

    public async ValueTask<bool> TryRememberAsync(string tokenId, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        cancellationToken.ThrowIfCancellationRequested();

        // The id comes from a token header chosen by the portal. Hashing keeps the key a fixed size and free of odd characters.
        string key = KeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId)));
        return await connection.GetDatabase().StringSetAsync(key, "1", window, When.NotExists);
    }

    public void Dispose() => connection.Dispose();
}
