using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Attention;

/// <summary>
/// A per-user budget for claim-level reads. The per-address limiter runs before sign-in, so it cannot tell users apart; this one is keyed on the portal user id
/// the session carries. Idle users' counters are dropped by the limiter itself, so memory follows the number of active users.
/// </summary>
internal sealed class ClaimReadLimiter(IOptions<PrismAuthOptions> auth) : IDisposable
{
    private readonly PartitionedRateLimiter<long> limiter = PartitionedRateLimiter.Create<long, long>(userId =>
        RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, auth.Value.ClaimReadsPerUserPerMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    public bool TryAcquire(long portalUserId)
    {
        using RateLimitLease lease = limiter.AttemptAcquire(portalUserId);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
