using Prism.Api.Metrics;
using Prism.Application.Auth;
using Prism.Application.SavedViews;

namespace Prism.Api.SavedViews;

/// <summary>
/// The caller's pushed views, to hide or restore from their settings. The Provider is the route's, checked against the caller's assignments, and the
/// owner is the session's: no body or route value names another user. Hiding never deletes, so a hidden view is not delivered again.
/// </summary>
internal static class PushedViewEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/settings/pushed-views")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Settings");

        group.MapGet("/", List)
            .WithName("ListPushedViews")
            .WithSummary("The views the Prism team pushed to the caller")
            .WithDescription("The caller's own views that arrived by a push, hidden ones included, by name. Up to 200. Returns 404 for a Provider the caller is not assigned to.")
            .Produces<SavedViewListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{viewId:int}/hide", (string providerId, int viewId, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken ct) =>
                SetHidden(providerId, viewId, true, http, access, store, ct))
            .WithName("HidePushedView")
            .WithSummary("Hide a pushed view")
            .WithDescription("Takes the view out of the caller's Saved Views list without deleting it, so it is not delivered again. Hiding twice is not an error. Returns 404 for a view that is not the caller's own pushed view.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{viewId:int}/restore", (string providerId, int viewId, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken ct) =>
                SetHidden(providerId, viewId, false, http, access, store, ct))
            .WithName("RestorePushedView")
            .WithSummary("Bring a hidden pushed view back")
            .WithDescription("Puts the view back in the caller's Saved Views list. Returns 404 for a view that is not the caller's own pushed view.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> List(string providerId, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken)
    {
        if (await SavedViewEndpoints.ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        IReadOnlyList<SavedViewSummary> pushed = await store.ListPushedAsync(actor, cancellationToken);
        return Results.Ok(new SavedViewListResponse([.. pushed.Select(s => new SummaryResponse(s.Id, s.Name, s.Description, s.OwnerName, s.IsOwner, s.WidgetCount, s.UpdatedAt) { FromTemplate = s.FromTemplate, IsHidden = s.IsHidden })], []));
    }

    private static async Task<IResult> SetHidden(string providerId, int viewId, bool hidden, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken)
    {
        if (await SavedViewEndpoints.ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        return await store.SetHiddenAsync(actor, viewId, hidden, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }
}
