using System.Collections.Concurrent;
using Prism.Application.Audit;

namespace Prism.Tests.Integration.Auth;

/// <summary>Keeps audit writes in memory so a test can assert what was recorded. <see cref="Failure"/> makes the next writes throw.</summary>
public sealed class FakeAuditLog : IAuditLog
{
    public ConcurrentQueue<AuditCall> Calls { get; } = new();

    public Exception? Failure { get; set; }

    public Task WriteAsync(int providerId, long portalUserId, string action, string checkKey, string period, int rowCount, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Calls.Enqueue(new AuditCall(providerId, portalUserId, action, checkKey, period, rowCount));
        return Task.CompletedTask;
    }
}

/// <summary>One audited read.</summary>
public sealed record AuditCall(int ProviderId, long PortalUserId, string Action, string CheckKey, string Period, int RowCount);
