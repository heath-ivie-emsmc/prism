using System.Collections.Concurrent;
using System.Text.Json;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Company (Provider) names, remembered across sign-ins. A name is kept for a day. A company the portal does not return is
/// remembered as unknown for five minutes, so a bad id cannot make every sign-in call the portal. A failed lookup is not
/// remembered, and never fails the caller: it just means no name this time.
/// </summary>
public sealed class PortalCompanyNames(TimeProvider timeProvider)
{
    internal const int MaxEntries = 50_000;

    private static readonly TimeSpan KnownFor = TimeSpan.FromHours(24);
    private static readonly TimeSpan UnknownFor = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <returns>The name, or null when the company has none or the lookup failed.</returns>
    public async Task<string?> NameAsync(string companyId, Func<CancellationToken, Task<string?>> lookup, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (entries.TryGetValue(companyId, out Entry? held) && held.ExpiresAt > now)
        {
            return held.Name;
        }

        string? name;
        try
        {
            name = await lookup(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return held?.Name;
        }

        Remember(companyId, name, now);
        return name;
    }

    private void Remember(string companyId, string? name, DateTimeOffset now)
    {
        if (entries.Count >= MaxEntries)
        {
            foreach (KeyValuePair<string, Entry> expired in entries.Where(pair => pair.Value.ExpiresAt <= now).Take(MaxEntries / 10))
            {
                entries.TryRemove(expired.Key, out _);
            }

            if (entries.Count >= MaxEntries)
            {
                return;
            }
        }

        entries[companyId] = new Entry(name, now + (name is null ? UnknownFor : KnownFor));
    }

    private sealed record Entry(string? Name, DateTimeOffset ExpiresAt);
}
