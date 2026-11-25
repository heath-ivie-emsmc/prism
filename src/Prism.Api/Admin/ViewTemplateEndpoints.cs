using System.Globalization;
using System.Security.Claims;
using Prism.Api.SavedViews;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Admin;

/// <summary>
/// View Templates and their Assignments, for Internal Users. Authoring needs the author tier; publishing and pushing need the publisher tier.
/// Nothing here reads Provider data: a Provider is only ever a number an admin names to push to, never resolved from a name.
/// </summary>
internal static class ViewTemplateEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/admin").WithTags("Admin");

        MapTemplates(group);
        MapAssignments(group);
    }

    private static void MapTemplates(RouteGroupBuilder group)
    {
        group.MapGet("/view-templates", List)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("ListViewTemplates")
            .WithSummary("The View Templates")
            .WithDescription("Every View Template by name, with whether it is published and how many widgets it has. A list never includes a layout. Needs the template author tier.")
            .Produces<ViewTemplateListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/view-templates/{templateId:int}", Get)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("GetViewTemplate")
            .WithSummary("One View Template with its layout")
            .WithDescription("The `ETag` header is the version to send back as `If-Match` when saving. Returns 404 for a template that does not exist.")
            .Produces<ViewTemplateResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/view-templates", Create)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("CreateViewTemplate")
            .WithSummary("Create a View Template")
            .WithDescription(
                "Creates an unpublished template. `name` is 1 to 80 characters and unique. `layout` follows the same rules as a Saved View layout, for example "
                + "`{\"version\":1,\"density\":\"comfortable\",\"widgets\":[{\"id\":\"w1\",\"metricKey\":\"net_collections\",\"presentation\":\"line\",\"options\":{\"months\":\"6\"},\"x\":0,\"y\":0,\"w\":6,\"h\":3}]}`. "
                + "A bad layout returns 400 with codes and never the values sent. At most 200 templates. The creation is audited.")
            .Produces<ViewTemplateResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/view-templates/{templateId:int}", Update)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("UpdateViewTemplate")
            .WithSummary("Save changes to a View Template")
            .WithDescription(
                "Send the `ETag` you loaded as `If-Match`: if the template was saved by someone else since, the answer is 412 and nothing changes. Without `If-Match` the answer is 428. "
                + "Editing a template never changes what an earlier push delivers, because a push keeps a snapshot. The save is audited.")
            .Produces<ViewTemplateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/view-templates/{templateId:int}/publish", Publish)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("PublishViewTemplate")
            .WithSummary("Publish a View Template")
            .WithDescription("Makes the template available to push and, later, to be a Persona's home page. Needs the template publisher tier. Publishing twice is not an error. The change is audited.")
            .Produces<ViewTemplateResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/view-templates/{templateId:int}/unpublish", Unpublish)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("UnpublishViewTemplate")
            .WithSummary("Take a View Template back to a draft")
            .WithDescription("A draft cannot be pushed. Pushes already made and copies already delivered are not affected. Needs the template publisher tier. The change is audited.")
            .Produces<ViewTemplateResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/view-templates/{templateId:int}", Delete)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("DeleteViewTemplate")
            .WithSummary("Delete a View Template")
            .WithDescription("Refused with 409 once the template has been pushed, so the record of what was pushed stays. Unpublish it instead. The deletion is audited.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapAssignments(RouteGroupBuilder group)
    {
        group.MapPost("/view-templates/{templateId:int}/assignments/preview", Preview)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("PreviewTemplateAssignment")
            .WithSummary("What a push would do, before confirming it")
            .WithDescription(
                "Example body: `{\"kind\":\"Provider\",\"providerId\":526}` or `{\"kind\":\"All\"}`. Returns the widget count to confirm and the number of Providers. "
                + "`providerCount` is null for `All`, because Prism has no list of every Provider. Sends and changes nothing. Needs the template publisher tier.")
            .Produces<AssignmentPreviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/view-templates/{templateId:int}/assignments", ListAssignments)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("ListTemplateAssignments")
            .WithSummary("The pushes of a View Template")
            .WithDescription("Each push with its audience, widget count, how many users have received it so far, and when it was revoked. Counts only: no user is named. Up to 200, newest first.")
            .Produces<AssignmentListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/view-templates/{templateId:int}/assignments", CreateAssignment)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("CreateTemplateAssignment")
            .WithSummary("Push a published View Template")
            .WithDescription(
                "Example body: `{\"scope\":{\"kind\":\"Provider\",\"providerId\":526},\"confirmedWidgetCount\":4}`. `confirmedWidgetCount` is the count from the preview: if the template changed since, the answer is 409 and nothing is pushed. "
                + "Each user receives their own copy the next time they open their Saved Views in the audience; a copy already delivered is never replaced. Only a published template can be pushed (409 otherwise). The push is audited.")
            .Produces<AssignmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/template-assignments/{assignmentId:int}", Revoke)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("RevokeTemplateAssignment")
            .WithSummary("Stop a push")
            .WithDescription("No further copies are delivered. Copies already delivered stay with their owners. Revoking twice is not an error. The revocation is audited.")
            .Produces<AssignmentResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>The Internal User from the session. Null when the session has no portal user id.</summary>
    internal static AdminActor? Actor(ClaimsPrincipal user)
    {
        if (!long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId))
        {
            return null;
        }

        string name = user.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        return new AdminActor(portalUserId, name.Length > 200 ? name[..200] : name);
    }

    internal static IResult NoIdentity() => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This session cannot be audited, so it cannot use the admin area.");

    private static string ETag(uint version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static ViewTemplateContent Content(ViewTemplateRequest body) =>
        new(body.Name!.Trim(), string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(), body.Layout!);

    private static async Task<IResult> List(IViewTemplateStore store, CancellationToken cancellationToken)
    {
        IReadOnlyList<ViewTemplateSummary> templates = await store.ListAsync(cancellationToken);
        return Results.Ok(new ViewTemplateListResponse([.. templates.Select(t => new ViewTemplateSummaryResponse(t.Id, t.Name, t.Description, t.IsPublished, t.WidgetCount, t.UpdatedAt))]));
    }

    private static async Task<IResult> Get(int templateId, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(templateId, cancellationToken) is not { } template)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = ETag(template.Version);
        return Results.Ok(Detail(template));
    }

    private static async Task<IResult> Create(ViewTemplateRequest? body, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        if (SavedViewEndpoints.ValidateContent(body?.Name, body?.Description, body?.Layout) is { } invalid)
        {
            return invalid;
        }

        ViewTemplateWriteResult result = await store.CreateAsync(actor, Content(body!), cancellationToken);
        if (result.Template is not { } template)
        {
            return WriteProblem(result.Status);
        }

        http.Response.Headers.ETag = ETag(template.Version);
        return Results.Created($"/api/v1/admin/view-templates/{template.Id}", Detail(template));
    }

    private static async Task<IResult> Update(int templateId, ViewTemplateRequest? body, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        if (!http.Request.Headers.TryGetValue("If-Match", out Microsoft.Extensions.Primitives.StringValues header))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Send the version you loaded in the If-Match header.");
        }

        if (!uint.TryParse(header.ToString().Trim().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out uint version))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The If-Match header is not a version this API issued.");
        }

        if (SavedViewEndpoints.ValidateContent(body?.Name, body?.Description, body?.Layout) is { } invalid)
        {
            return invalid;
        }

        ViewTemplateWriteResult result = await store.UpdateAsync(actor, templateId, version, Content(body!), cancellationToken);
        if (result.Template is not { } template)
        {
            return WriteProblem(result.Status);
        }

        http.Response.Headers.ETag = ETag(template.Version);
        return Results.Ok(Detail(template));
    }

    private static Task<IResult> Publish(int templateId, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken) =>
        SetPublished(templateId, true, http, store, cancellationToken);

    private static Task<IResult> Unpublish(int templateId, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken) =>
        SetPublished(templateId, false, http, store, cancellationToken);

    private static async Task<IResult> SetPublished(int templateId, bool published, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        ViewTemplateWriteResult result = await store.SetPublishedAsync(actor, templateId, published, cancellationToken);
        return result.Template is { } template ? Results.Ok(Detail(template)) : WriteProblem(result.Status);
    }

    private static async Task<IResult> Delete(int templateId, HttpContext http, IViewTemplateStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        ViewTemplateWriteResult result = await store.DeleteAsync(actor, templateId, cancellationToken);
        return result.Status == ViewTemplateWriteStatus.Saved ? Results.NoContent() : WriteProblem(result.Status);
    }

    private static async Task<IResult> Preview(int templateId, AssignmentScopeRequest? body, ITemplateAssignmentStore store, CancellationToken cancellationToken)
    {
        if (ParseScope(body) is not { } scope)
        {
            return ScopeProblem();
        }

        return await store.PreviewAsync(templateId, scope, cancellationToken) is { } preview
            ? Results.Ok(new AssignmentPreviewResponse(preview.ViewTemplateId, preview.TemplateName, preview.WidgetCount, ScopeResponse(preview.Scope), preview.ProviderCount))
            : Results.NotFound();
    }

    private static async Task<IResult> ListAssignments(int templateId, ITemplateAssignmentStore store, CancellationToken cancellationToken)
    {
        IReadOnlyList<AssignmentSummary> assignments = await store.ListAsync(templateId, cancellationToken);
        return Results.Ok(new AssignmentListResponse([.. assignments.Select(Assignment)]));
    }

    private static async Task<IResult> CreateAssignment(int templateId, CreateAssignmentRequest? body, HttpContext http, ITemplateAssignmentStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        if (ParseScope(body?.Scope) is not { } scope || body?.ConfirmedWidgetCount is not { } confirmed)
        {
            return ScopeProblem();
        }

        AssignmentWriteResult result = await store.CreateAsync(actor, templateId, scope, confirmed, cancellationToken);
        return result.Assignment is { } assignment
            ? Results.Created($"/api/v1/admin/template-assignments/{assignment.Id}", Assignment(assignment))
            : AssignmentProblem(result.Status);
    }

    private static async Task<IResult> Revoke(int assignmentId, HttpContext http, ITemplateAssignmentStore store, CancellationToken cancellationToken)
    {
        if (Actor(http.User) is not { } actor)
        {
            return NoIdentity();
        }

        AssignmentWriteResult result = await store.RevokeAsync(actor, assignmentId, cancellationToken);
        return result.Assignment is { } assignment ? Results.Ok(Assignment(assignment)) : AssignmentProblem(result.Status);
    }

    /// <summary>The audience, or null when it is not one of the two kinds or its Provider is missing, not a positive number, or given for everyone.</summary>
    private static AssignmentScope? ParseScope(AssignmentScopeRequest? body) => body switch
    {
        { Kind: AssignmentScopeKinds.All, ProviderId: null } => new AssignmentScope(AssignmentScopeKinds.All, null),
        { Kind: AssignmentScopeKinds.Provider, ProviderId: > 0 and { } providerId } => new AssignmentScope(AssignmentScopeKinds.Provider, providerId),
        _ => null,
    };

    private static IResult ScopeProblem() => Results.ValidationProblem(new Dictionary<string, string[]> { ["scope"] = ["invalid_scope"] });

    private static IResult WriteProblem(ViewTemplateWriteStatus status) => status switch
    {
        ViewTemplateWriteStatus.NameTaken => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A template with this name already exists."),
        ViewTemplateWriteStatus.LimitReached => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The number of templates has reached its limit."),
        ViewTemplateWriteStatus.HasAssignments => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This template has been pushed, so it cannot be deleted. Unpublish it instead."),
        ViewTemplateWriteStatus.Conflict => Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "This template was saved by someone else since you opened it."),
        _ => Results.NotFound(),
    };

    private static IResult AssignmentProblem(AssignmentWriteStatus status) => status switch
    {
        AssignmentWriteStatus.NotPublished => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a published template can be pushed."),
        AssignmentWriteStatus.StaleConfirmation => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The template changed since you previewed it. Preview it again."),
        AssignmentWriteStatus.LimitReached => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This template has reached the number of pushes it can have."),
        _ => Results.NotFound(),
    };

    private static ViewTemplateResponse Detail(ViewTemplateDetail t) =>
        new(t.Id, t.Name, t.Description, t.IsPublished, t.Layout, t.Version.ToString(CultureInfo.InvariantCulture), t.UpdatedAt);

    private static AssignmentScopeResponse ScopeResponse(AssignmentScope scope) => new(scope.Kind, scope.ProviderId);

    private static AssignmentResponse Assignment(AssignmentSummary a) =>
        new(a.Id, a.ViewTemplateId, ScopeResponse(a.Scope), a.Name, a.WidgetCount, a.DeliveredCount, a.CreatedAt, a.RevokedAt);
}

/// <summary>A template to save. Only these three fields are read: anything else in the body is ignored.</summary>
public sealed record ViewTemplateRequest(string? Name, string? Description, SavedViewLayout? Layout);

public sealed record ViewTemplateSummaryResponse(int Id, string Name, string? Description, bool IsPublished, int WidgetCount, DateTimeOffset UpdatedAt);

public sealed record ViewTemplateListResponse(IReadOnlyList<ViewTemplateSummaryResponse> Templates);

/// <summary>A template with its layout. <c>Version</c> is the same value as the <c>ETag</c> header, without the quotes.</summary>
public sealed record ViewTemplateResponse(int Id, string Name, string? Description, bool IsPublished, SavedViewLayout Layout, string Version, DateTimeOffset UpdatedAt);

/// <summary>Who a push is for: <c>All</c> (no <c>providerId</c>) or <c>Provider</c> (a positive <c>providerId</c>).</summary>
public sealed record AssignmentScopeRequest(string? Kind, int? ProviderId);

public sealed record CreateAssignmentRequest(AssignmentScopeRequest? Scope, int? ConfirmedWidgetCount);

public sealed record AssignmentScopeResponse(string Kind, int? ProviderId);

public sealed record AssignmentPreviewResponse(int ViewTemplateId, string TemplateName, int WidgetCount, AssignmentScopeResponse Scope, int? ProviderCount);

public sealed record AssignmentResponse(int Id, int ViewTemplateId, AssignmentScopeResponse Scope, string Name, int WidgetCount, int DeliveredCount, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record AssignmentListResponse(IReadOnlyList<AssignmentResponse> Assignments);
