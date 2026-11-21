using System.Collections.Concurrent;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Single-instance replay store. It does not protect a multi-instance deployment: use the Redis implementation there.
/// Entries expire with their window and the table has a hard size cap.
/// </summary>
public sealed class InMemoryLoginTokenReplayStore(TimeProvider timeProvider) : ILoginTokenReplayStore
{
    internal const int MaxEntries = 100_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);

    public ValueTask<bool> TryRememberAsync(string tokenId, TimeSpan window, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (seen.Count >= MaxEntries)
        {
            Sweep(now);
        }

        if (seen.Count >= MaxEntries)
        {
            // Fail closed: when full, refuse new sign-ins instead of forgetting ids that could then be replayed.
            return ValueTask.FromResult(false);
        }

        DateTimeOffset expires = now + window;
        bool added = true;
        seen.AddOrUpdate(
            tokenId,
            _ => expires,
            (_, existing) =>
            {
                if (existing > now)
                {
                    added = false;
                    return existing;
                }

                return expires;
            });

        return ValueTask.FromResult(added);
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, DateTimeOffset> entry in seen.Where(entry => entry.Value <= now))
        {
            seen.TryRemove(entry);
        }
    }
}
