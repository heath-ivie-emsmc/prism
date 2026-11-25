using System.Collections.Concurrent;

namespace Prism.Infrastructure.Customers;

/// <summary>
/// Remembers what a user may do in a Provider for a short time, so a role is not read on every request. Shared by every request, size-capped, and cleared
/// for a user the moment their role changes here. Another instance picks a change up when its entry expires, which is the longest a live session can lag.
/// </summary>
public sealed class CustomerRoleCache(TimeProvider timeProvider)
{
    internal const int MaxEntries = 20_000;

    public static readonly TimeSpan LifeTime = TimeSpan.FromSeconds(30);

    /// <summary>The key for "any Provider".</summary>
    internal const int AnyProvider = 0;

    private readonly ConcurrentDictionary<(int ProviderId, long UserId), Entry> entries = new();

    public bool TryGet(int providerId, long userId, out IReadOnlySet<string> permissions)
    {
        if (entries.TryGetValue((providerId, userId), out Entry? held) && held.ExpiresAt > timeProvider.GetUtcNow())
        {
            permissions = held.Permissions;
            return true;
        }

        permissions = new HashSet<string>();
        return false;
    }

    public void Set(int providerId, long userId, IReadOnlySet<string> permissions)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (entries.Count >= MaxEntries)
        {
            foreach (KeyValuePair<(int, long), Entry> expired in entries.Where(pair => pair.Value.ExpiresAt <= now).Take(MaxEntries / 10))
            {
                entries.TryRemove(expired.Key, out _);
            }

            if (entries.Count >= MaxEntries)
            {
                return;
            }
        }

        entries[(providerId, userId)] = new Entry(permissions, now + LifeTime);
    }

    /// <summary>Forgets a user in a Provider and in "any Provider".</summary>
    public void Forget(int providerId, long userId)
    {
        entries.TryRemove((providerId, userId), out _);
        entries.TryRemove((AnyProvider, userId), out _);
    }

    private sealed record Entry(IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAt);
}
