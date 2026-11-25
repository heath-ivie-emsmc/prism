using Prism.Application.SavedViews;

namespace Prism.Application.Admin;

public static class ViewTemplateLimits
{
    public const int MaxTemplates = 200;
    public const int MaxAssignmentsPerTemplate = 200;
    public const int MaxDeliveriesPerRequest = 10;
    public const int MaxProviderId = int.MaxValue;
}

/// <summary>The parts of a View Template an author edits. The layout is validated against the Metric Catalog like a Saved View's.</summary>
public sealed record ViewTemplateContent(string Name, string? Description, SavedViewLayout Layout);

public sealed record ViewTemplateSummary(int Id, string Name, string? Description, bool IsPublished, int WidgetCount, DateTimeOffset UpdatedAt);

/// <summary><paramref name="Version"/> is the concurrency token to send back as <c>If-Match</c>.</summary>
public sealed record ViewTemplateDetail(int Id, string Name, string? Description, bool IsPublished, SavedViewLayout Layout, uint Version, DateTimeOffset UpdatedAt);

public enum ViewTemplateWriteStatus
{
    Saved,
    NotFound,
    NameTaken,
    Conflict,
    LimitReached,

    /// <summary>Deleting is refused while the template has Assignments, so the history of what was pushed stays.</summary>
    HasAssignments,
}

public sealed record ViewTemplateWriteResult(ViewTemplateWriteStatus Status, ViewTemplateDetail? Template = null);

/// <summary>View Templates. The caller has already been authorized for the author tier; nothing here is Provider data.</summary>
public interface IViewTemplateStore
{
    Task<IReadOnlyList<ViewTemplateSummary>> ListAsync(CancellationToken cancellationToken);

    Task<ViewTemplateDetail?> GetAsync(int templateId, CancellationToken cancellationToken);

    Task<ViewTemplateWriteResult> CreateAsync(AdminActor actor, ViewTemplateContent content, CancellationToken cancellationToken);

    /// <summary><paramref name="expectedVersion"/> is the version the editor loaded: a different stored version is a <see cref="ViewTemplateWriteStatus.Conflict"/>.</summary>
    Task<ViewTemplateWriteResult> UpdateAsync(AdminActor actor, int templateId, uint expectedVersion, ViewTemplateContent content, CancellationToken cancellationToken);

    Task<ViewTemplateWriteResult> SetPublishedAsync(AdminActor actor, int templateId, bool published, CancellationToken cancellationToken);

    Task<ViewTemplateWriteResult> DeleteAsync(AdminActor actor, int templateId, CancellationToken cancellationToken);
}
