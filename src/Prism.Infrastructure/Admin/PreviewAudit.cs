using Microsoft.Extensions.Caching.Memory;
using Prism.Application.Admin;
using Prism.Infrastructure.Persistence;

namespace Prism.Infrastructure.Admin;

/// <summary>Remembers which Providers a session has already opened, so each one is audited once per session. Bounded by size and by the session lifetime.</summary>
public sealed class PreviewAuditGate : IDisposable
{
    /// <summary>Most (session, Provider) pairs remembered. Beyond it an entry is audited again, never skipped.</summary>
    public const int MaxEntries = 10_000;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    public bool Seen(string key) => cache.TryGetValue(key, out _);

    public void Remember(string key, TimeSpan lifetime) =>
        cache.Set(key, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = lifetime });

    public void Dispose() => cache.Dispose();
}

/// <summary>Writes the audit row for opening a Provider's figures in preview, once per session and Provider.</summary>
public sealed class PreviewAudit(PrismDbContext db, PreviewAuditGate gate) : IPreviewAudit
{
    /// <summary>Longer than a session (8 hours), so a session never outlives its own entry.</summary>
    private static readonly TimeSpan Remembered = TimeSpan.FromHours(9);

    private const int MaxSessionIdLength = 64;

    public async Task<bool> RecordAsync(AdminActor actor, int providerId, string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId) || sessionId.Length > MaxSessionIdLength || providerId < 1)
        {
            return false;
        }

        string key = $"{sessionId}:{providerId}";
        if (gate.Seen(key))
        {
            return true;
        }

        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.ProviderPreviewed, AdminAuditObjects.Provider, null, providerId));
        await db.SaveChangesAsync(cancellationToken);
        gate.Remember(key, Remembered);
        return true;
    }
}
