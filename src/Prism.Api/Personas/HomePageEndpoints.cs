using Prism.Api.Metrics;
using Prism.Api.SavedViews;
using Prism.Application.Auth;
using Prism.Application.Personas;
using Prism.Application.SavedViews;

namespace Prism.Api.Personas;

/// <summary>
/// The signed-in user's home page in a Provider. The Provider is the route's, checked against the caller's assignments; the user is the session's.
/// The Persona's View Template is followed live until the user customizes it, which copies it into their own Saved Views.
/// </summary>
internal static class HomePageEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/home")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Home page");

        group.MapGet("/", Get)
            .WithName("GetHomePage")
            .WithSummary("Where the user's home page comes from")
            .WithDescription(
                "`source` is `custom` (the user's own copy: open Saved View `savedViewId`), `persona` (the Persona's current published View Template: draw `layout`) "
                + "or `default` (the Service Line default home page). The layout holds Metric keys and positions only. Returns 404 for a Provider the user is not assigned to.")
            .Produces<HomePageResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/customize", Customize)
            .WithName("CustomizeHomePage")
            .WithSummary("Make the home page the user's own")
            .WithDescription(
                "Copies the Persona's View Template into the user's Saved Views as their home page in this Provider, and returns the new Saved View to open. Later template edits no longer change it. "
                + "Calling it again returns the existing copy. Returns 409 when there is no Persona template to copy or the user has too many views.")
            .Produces<HomeCustomizeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/customize", Reset)
            .WithName("ResetHomePage")
            .WithSummary("Go back to the Persona's home page")
            .WithDescription("Deletes the user's own copy, so the home page follows the Persona's current published View Template again. Returns 404 when there is no copy.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Get(string providerId, HttpContext http, ProviderAccess access, IHomePage home, CancellationToken cancellationToken)
    {
        if (await SavedViewEndpoints.ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        HomeResolution resolution = await home.ResolveAsync(actor, cancellationToken);
        return Results.Ok(new HomePageResponse(resolution.Source, resolution.Layout, resolution.SavedViewId));
    }

    private static async Task<IResult> Customize(string providerId, HttpContext http, ProviderAccess access, IHomePage home, CancellationToken cancellationToken)
    {
        if (await SavedViewEndpoints.ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        HomeCustomizeResult result = await home.CustomizeAsync(actor, cancellationToken);
        return result switch
        {
            { Status: HomeCustomizeStatus.Customized, SavedViewId: { } id } => Results.Ok(new HomeCustomizeResponse(id)),
            { Status: HomeCustomizeStatus.LimitReached } => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You have the most views one user may keep here. Delete one first."),
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "There is no home page to copy."),
        };
    }

    private static async Task<IResult> Reset(string providerId, HttpContext http, ProviderAccess access, IHomePage home, CancellationToken cancellationToken)
    {
        if (await SavedViewEndpoints.ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        return await home.ResetAsync(actor, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }
}

public sealed record HomePageResponse(string Source, SavedViewLayout? Layout, int? SavedViewId);

public sealed record HomeCustomizeResponse(int SavedViewId);
