using Microsoft.EntityFrameworkCore;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Persistence;

public sealed class SessionRevocationStore(PrismDbContext db, TimeProvider timeProvider) : ISessionRevocationStore
{
    /// <summary>Must exceed the session token clock skew (2 minutes) so a revoked session cannot come back to life.</summary>
    public static readonly TimeSpan PurgeAfterExpiry = TimeSpan.FromMinutes(10);

    public async Task RevokeAsync(string sessionId, string userName, DateTimeOffset sessionExpiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // One atomic statement: a second revoke of the same session is a no-op instead of a unique violation.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO revoked_session (session_id, user_name, session_expires_at_timestamp)
            VALUES ({sessionId}, {userName}, {sessionExpiresAt})
            ON CONFLICT (session_id) DO NOTHING
            """,
            cancellationToken);

        // Once a session has expired on its own, its revocation row can no longer matter. The session token is still accepted
        // for the clock skew after expiry, so rows are kept for that long plus a margin.
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - PurgeAfterExpiry;
        await db.RevokedSessions
            .Where(r => r.SessionExpiresAtTimestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken) =>
        db.RevokedSessions.AsNoTracking().AnyAsync(r => r.SessionId == sessionId, cancellationToken);
}
