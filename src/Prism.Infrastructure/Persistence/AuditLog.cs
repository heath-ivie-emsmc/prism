using Prism.Application.Audit;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Persistence;

public sealed class AuditLog(PrismDbContext db) : IAuditLog
{
    public async Task WriteAsync(int providerId, long portalUserId, string action, string checkKey, string period, int rowCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(period);

        db.AuditEntries.Add(new AuditEntry
        {
            ProviderId = providerId,
            PortalUserId = portalUserId,
            Action = action,
            CheckKey = checkKey,
            Period = period,
            RowCount = rowCount,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
