namespace Prism.Application.Admin;

public static class AssignmentScopeKinds
{
    public const string All = "All";
    public const string Provider = "Provider";
}

/// <summary>Who an Assignment is for. <paramref name="ProviderId"/> is set only for the <c>Provider</c> kind.</summary>
public sealed record AssignmentScope(string Kind, int? ProviderId);

/// <summary>
/// What a push would do, shown before it is confirmed. <paramref name="ProviderCount"/> is null for everyone, because Prism has no list of all
/// Providers: they live in the portal.
/// </summary>
public sealed record AssignmentPreview(int ViewTemplateId, string TemplateName, int WidgetCount, AssignmentScope Scope, int? ProviderCount);

public sealed record AssignmentSummary(
    int Id,
    int ViewTemplateId,
    AssignmentScope Scope,
    string Name,
    int WidgetCount,
    int DeliveredCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public enum AssignmentWriteStatus
{
    Saved,
    TemplateNotFound,

    /// <summary>Only a published template can be pushed.</summary>
    NotPublished,

    /// <summary>The confirmed widget count no longer matches the template, so the admin confirmed something that has changed.</summary>
    StaleConfirmation,

    LimitReached,
    NotFound,
}

public sealed record AssignmentWriteResult(AssignmentWriteStatus Status, AssignmentSummary? Assignment = null);

/// <summary>Assignments of View Templates. Only published templates can be pushed, and a push keeps a snapshot of the template as it was.</summary>
public interface ITemplateAssignmentStore
{
    Task<AssignmentPreview?> PreviewAsync(int templateId, AssignmentScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentSummary>> ListAsync(int templateId, CancellationToken cancellationToken);

    /// <summary>Pushes a published template. The server recomputes the widget count and refuses a stale confirmation.</summary>
    Task<AssignmentWriteResult> CreateAsync(AdminActor actor, int templateId, AssignmentScope scope, int confirmedWidgetCount, CancellationToken cancellationToken);

    /// <summary>Stops future deliveries. Copies already delivered stay with their owners. Revoking twice is not an error.</summary>
    Task<AssignmentWriteResult> RevokeAsync(AdminActor actor, int assignmentId, CancellationToken cancellationToken);
}
