namespace Prism.Application.SavedViews;

/// <summary>Who is acting and in which Provider. Built by server code from the checked route Provider and the session, never from a request body.</summary>
public sealed record SavedViewActor(int ProviderId, long PortalUserId, string UserName);

/// <summary>The parts of a view the owner edits.</summary>
public sealed record SavedViewContent(string Name, string? Description, SavedViewLayout Layout);

/// <summary>What the lists show about a view. No layout, so a list stays small.</summary>
public sealed record SavedViewSummary(int Id, string Name, string? Description, string? OwnerName, bool IsOwner, int WidgetCount, DateTimeOffset UpdatedAt, bool FromTemplate = false, bool IsHidden = false);

/// <summary>A view to open. <paramref name="Version"/> is the concurrency token to send back as <c>If-Match</c>. <paramref name="IsOwner"/> is false for a recipient, who may not change it.</summary>
public sealed record SavedViewDetail(int Id, string Name, string? Description, string? OwnerName, bool IsOwner, SavedViewLayout Layout, uint Version, DateTimeOffset UpdatedAt);

/// <summary>The views a user can open in a Provider: their own, and the ones shared with them.</summary>
public sealed record SavedViewLists(IReadOnlyList<SavedViewSummary> Mine, IReadOnlyList<SavedViewSummary> Shared);

/// <summary>What a write to a view did.</summary>
public enum SavedViewWriteStatus
{
    Saved,
    NotFound,
    NameTaken,
    Conflict,

    /// <summary>The owner already has the most views one user may keep in a Provider.</summary>
    LimitReached,
}

public sealed record SavedViewWriteResult(SavedViewWriteStatus Status, SavedViewDetail? View = null);

/// <summary>
/// Saved Views, always scoped by the Provider and the signed-in user, both passed in by server code from the route (checked against the user's assignments)
/// and the session. A view that is missing, in another Provider, or not the user's to see is the same answer: null or <see cref="SavedViewWriteStatus.NotFound"/>.
/// </summary>
public interface ISavedViewStore
{
    Task<SavedViewLists> ListAsync(SavedViewActor actor, CancellationToken cancellationToken);

    Task<SavedViewDetail?> GetAsync(SavedViewActor actor, int viewId, CancellationToken cancellationToken);

    Task<SavedViewWriteResult> CreateAsync(SavedViewActor actor, SavedViewContent content, CancellationToken cancellationToken);

    /// <summary>Owner only. <paramref name="expectedVersion"/> is the version the editor loaded: a different stored version is a <see cref="SavedViewWriteStatus.Conflict"/>.</summary>
    Task<SavedViewWriteResult> UpdateAsync(SavedViewActor actor, int viewId, uint expectedVersion, SavedViewContent content, CancellationToken cancellationToken);

    /// <summary>Owner only. Removes the view with its shares and invitations. Returns false when there was no such view for this owner.</summary>
    Task<bool> DeleteAsync(SavedViewActor actor, int viewId, CancellationToken cancellationToken);

    /// <summary>The caller's own views that arrived by a push, hidden ones included, so they can be restored. Bounded.</summary>
    Task<IReadOnlyList<SavedViewSummary>> ListPushedAsync(SavedViewActor actor, CancellationToken cancellationToken);

    /// <summary>Hides a pushed view from the caller's list, or brings it back. Only the owner's pushed views qualify: false for any other view.</summary>
    Task<bool> SetHiddenAsync(SavedViewActor actor, int viewId, bool hidden, CancellationToken cancellationToken);
}
