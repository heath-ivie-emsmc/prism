using Prism.Application.Audit;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Persistence;

public sealed class AuditLog(PrismDbContext db) : IAuditLog
{
    public Task WriteAsync(int providerId, long portalUserId, AuditRead read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(read.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(read.CheckKey);

        return SaveAsync(providerId, portalUserId, read, cancellationToken);
    }

    private async Task SaveAsync(int providerId, long portalUserId, AuditRead read, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            ProviderId = providerId,
            PortalUserId = portalUserId,
            Action = read.Action,
            CheckKey = read.CheckKey,
            Period = read.Period.ToString(),
            RowCount = read.Rows,
            GroupKey = read.GroupKey,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
