namespace Prism.Application.Admin;

/// <summary>
/// Records that an Internal User opened a Provider's aggregate figures to lay out a View Template, before any figure is read. A session that
/// opens a Provider is recorded once. If the record cannot be written the answer is false and the read is refused.
/// </summary>
public interface IPreviewAudit
{
    Task<bool> RecordAsync(AdminActor actor, int providerId, string sessionId, CancellationToken cancellationToken);
}
