using System.Collections.Concurrent;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>Remembers each user's Providers for a few minutes, across requests. Shared by every request.</summary>
public sealed class UserScopeCache(TimeProvider timeProvider)
{
    internal const int MaxUsers = 10_000;

    public static readonly TimeSpan LifeTime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<long, Entry> entries = new();

    public async Task<IReadOnlyList<ProviderScope>> GetOrLoadAsync(
        long portalUserId,
        Func<CancellationToken, Task<IReadOnlyList<ProviderScope>>> load,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (entries.TryGetValue(portalUserId, out Entry? held) && held.ExpiresAt > now)
        {
            return held.Providers;
        }

        // A failure is not remembered and is not papered over with an old answer: a user whose scope cannot be read has no scope.
        IReadOnlyList<ProviderScope> providers = await load(cancellationToken);
        Store(portalUserId, providers, now);
        return providers;
    }

    private void Store(long portalUserId, IReadOnlyList<ProviderScope> providers, DateTimeOffset now)
    {
        if (entries.Count >= MaxUsers)
        {
            foreach (KeyValuePair<long, Entry> expired in entries.Where(pair => pair.Value.ExpiresAt <= now).Take(MaxUsers / 10))
            {
                entries.TryRemove(expired.Key, out _);
            }

            if (entries.Count >= MaxUsers)
            {
                return;
            }
        }

        entries[portalUserId] = new Entry(providers, now + LifeTime);
    }

    private sealed record Entry(IReadOnlyList<ProviderScope> Providers, DateTimeOffset ExpiresAt);
}

public sealed class UserScopeProvider(IPortalDirectory directory, UserScopeCache cache) : IUserScopeProvider
{
    public Task<IReadOnlyList<ProviderScope>> GetProvidersAsync(long portalUserId, CancellationToken cancellationToken) =>
        cache.GetOrLoadAsync(portalUserId, token => directory.ResolveProvidersAsync(portalUserId, token), cancellationToken);
}
