using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Prism.Api.Metrics;
using Prism.Application.Auth;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Auth;

namespace Prism.Api.SavedViews;

/// <summary>
/// Saved Views: dashboard layouts a user arranged and saved, shared by emailed invitation. The Provider is the route's, checked against the caller's
/// assignments on every request. The owner and Provider are never read from a body. A layout holds Metric keys and positions only.
/// </summary>
internal static class SavedViewEndpoints
{
    public const string InvitationRateLimitPolicy = "saved-view-invitation";

    private static readonly JsonSerializerOptions LayoutJson = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/saved-views")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Saved views");

        MapViews(group);
        MapSharing(group);

        v1.MapPost("/saved-view-invitations/redeem", Redeem)
            .RequireAuthorization(Permissions.DashboardView)
            .RequireRateLimiting(InvitationRateLimitPolicy)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithTags("Saved views")
            .WithName("RedeemSavedViewInvitation")
            .WithSummary("Open a shared Saved View from an emailed link")
            .WithDescription(
                "Uses up the invitation `token` from the emailed link and gives the signed-in user read access to that view. The token works once, until it expires, and not after the owner revokes it. "
                + "The caller must be assigned to the view's Provider. Every failure (unknown, used, expired, revoked or out-of-scope) returns the same 404, so nothing says whether a token, a view or an address exists. "
                + "Returns the Provider and view to open. Example body: `{\"token\":\"<43 characters from the link>\"}`.")
            .Produces<RedeemResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static void MapViews(RouteGroupBuilder group)
    {
        group.MapGet("/", List)
            .WithName("ListSavedViews")
            .WithSummary("The caller's Saved Views and the ones shared with them")
            .WithDescription("`mine` are the views the caller owns and `shared` the ones shared with them, each by name. A list never includes a layout. Up to 200 of each, by name.")
            .Produces<SavedViewListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{viewId:int}", Get)
            .WithName("GetSavedView")
            .WithSummary("One Saved View with its layout")
            .WithDescription(
                "The view if the caller owns it or it was shared with them, otherwise 404 (a view that is missing, in another Provider or not theirs look the same). "
                + "`isOwner` is false for a recipient, who can open it but not change it. The `ETag` header is the version to send back as `If-Match` when saving.")
            .Produces<SavedViewResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", Create)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("CreateSavedView")
            .WithSummary("Save a new view")
            .WithDescription(
                "Creates a view owned by the caller. `name` is 1 to 80 characters and unique among the caller's views in this Provider. `layout` is `{\"version\":1,\"density\":\"comfortable\",\"widgets\":[{\"id\":\"w1\",\"metricKey\":\"net_collections\",\"presentation\":\"line\",\"options\":{\"months\":\"6\"},\"x\":0,\"y\":0,\"w\":6,\"h\":3}]}`: "
                + "at most 24 widgets on 12 columns, each a Metric from the catalog that is available and placeable, drawn one of the ways the catalog allows, with options and sizes the catalog allows. "
                + "A bad layout returns 400 with codes and never the values sent. An `ownerId` or `providerId` in the body is ignored. At most 100 views per owner and Provider.")
            .Produces<SavedViewResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{viewId:int}", Update)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("UpdateSavedView")
            .WithSummary("Save changes to a view")
            .WithDescription(
                "Owner only. Send the `ETag` you loaded as `If-Match`: if the view was saved by someone else since, the answer is 412 and nothing changes. Without `If-Match` the answer is 428. Same body rules as creating a view.")
            .Produces<SavedViewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapDelete("/{viewId:int}", Delete)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithName("DeleteSavedView")
            .WithSummary("Delete a view")
            .WithDescription("Owner only. Removes the view with its recipients and pending invitations. Recipients lose access at once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapSharing(RouteGroupBuilder group)
    {
        group.MapPost("/{viewId:int}/invitations", Invite)
            .RequireAuthorization(Permissions.ViewsManage)
            .RequireRateLimiting(InvitationRateLimitPolicy)
            .WithMetadata(new RequestSizeLimitMetadata(SavedViewLimits.MaxRequestBodyBytes))
            .WithName("InviteToSavedView")
            .WithSummary("Email an invitation to open a view")
            .WithDescription(
                "Owner only. Emails each address in `emails` (at most 10 a request) a link that works once and expires. Example body: `{\"emails\":[\"colleague@example.org\"]}`. "
                + "An address with a pending invitation gets a fresh one and the old link stops working. The email names the view and the sender only. `sent` are the invitations made and `failed` the addresses whose email could not be sent (their invitations are revoked). "
                + "A view has at most 50 recipients and pending invitations, and an owner can send 30 an hour. Returns 503 when invitations are not turned on for this host or no email could be sent.")
            .Produces<InviteResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{viewId:int}/shares", Shares)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithName("ListSavedViewShares")
            .WithSummary("Who can open a view, and the invitations still waiting")
            .WithDescription("Owner only. `recipients` have redeemed an invitation (`email` is the address it went to). `pending` have not been used, revoked or (when `isExpired`) have run out.")
            .Produces<SharingResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{viewId:int}/shares/{shareId:int}", RevokeShare)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithName("RevokeSavedViewShare")
            .WithSummary("Remove a recipient")
            .WithDescription("Owner only. The recipient can no longer open the view.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{viewId:int}/invitations/{invitationId:int}", RevokeInvitation)
            .RequireAuthorization(Permissions.ViewsManage)
            .WithName("RevokeSavedViewInvitation")
            .WithSummary("Cancel a pending invitation")
            .WithDescription("Owner only. The emailed link stops working.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>The caller as the stores want them, or null when the Provider is not one they may read in full or the session has no portal user id.</summary>
    internal static async Task<SavedViewActor?> ActorAsync(HttpContext http, string providerId, ProviderAccess access, CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(http.User, providerId, cancellationToken) is not { } id
            || !long.TryParse(http.User.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId))
        {
            return null;
        }

        string name = http.User.FindFirstValue(SessionTokenIssuer.SubjectClaim) ?? "";
        return new SavedViewActor(id, portalUserId, name.Length > 200 ? name[..200] : name);
    }

    public static async Task<IResult> List(string providerId, HttpContext http, ProviderAccess access, ISavedViewStore store, Prism.Application.Admin.ITemplateDelivery delivery, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        // Pushed View Templates arrive here, the first time the user opens their views after a push.
        await delivery.DeliverPendingAsync(actor, cancellationToken);
        SavedViewLists lists = await store.ListAsync(actor, cancellationToken);
        return Results.Ok(new SavedViewListResponse([.. lists.Mine.Select(Summary)], [.. lists.Shared.Select(Summary)]));
    }

    public static async Task<IResult> Get(string providerId, int viewId, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor
            || await store.GetAsync(actor, viewId, cancellationToken) is not { } view)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = ETag(view.Version);
        return Results.Ok(Detail(view));
    }

    public static async Task<IResult> Create(string providerId, SavedViewRequest? body, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        if (Validate(body) is { } invalid)
        {
            return invalid;
        }

        SavedViewWriteResult result = await store.CreateAsync(actor, Content(body!), cancellationToken);
        if (result.View is not { } view)
        {
            return WriteProblem(result.Status);
        }

        http.Response.Headers.ETag = ETag(view.Version);
        return Results.Created($"/api/v1/providers/{Uri.EscapeDataString(providerId)}/saved-views/{view.Id}", Detail(view));
    }

    public static async Task<IResult> Update(string providerId, int viewId, SavedViewRequest? body, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        if (!http.Request.Headers.TryGetValue("If-Match", out Microsoft.Extensions.Primitives.StringValues header))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Send the version you loaded in the If-Match header.");
        }

        if (!uint.TryParse(header.ToString().Trim().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out uint version))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The If-Match header is not a version this API issued.");
        }

        if (Validate(body) is { } invalid)
        {
            return invalid;
        }

        SavedViewWriteResult result = await store.UpdateAsync(actor, viewId, version, Content(body!), cancellationToken);
        if (result.View is not { } view)
        {
            return WriteProblem(result.Status);
        }

        http.Response.Headers.ETag = ETag(view.Version);
        return Results.Ok(Detail(view));
    }

    public static async Task<IResult> Delete(string providerId, int viewId, HttpContext http, ProviderAccess access, ISavedViewStore store, CancellationToken cancellationToken) =>
        await ActorAsync(http, providerId, access, cancellationToken) is { } actor && await store.DeleteAsync(actor, viewId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    public static async Task<IResult> Invite(string providerId, int viewId, InviteRequest? body, HttpContext http, ProviderAccess access, InvitationService invitations, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor)
        {
            return Results.NotFound();
        }

        if (ParseEmails(body) is not { } emails)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["emails"] = ["Send between 1 and 10 valid email addresses."] });
        }

        InviteResult result = await invitations.InviteAsync(actor, viewId, emails, cancellationToken);
        return result.Status switch
        {
            InviteStatus.Sent => Results.Ok(new InviteResponse([.. result.Sent.Select(p => new PendingInvitationResponse(p.InvitationId, p.Email, p.ExpiresAt, p.IsExpired))], result.Failed)),
            InviteStatus.NotFound => Results.NotFound(),
            InviteStatus.TooManyRecipients => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A view can have up to 50 people and pending invitations."),
            InviteStatus.TooManyInvitations => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many invitations sent recently. Try again later."),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Invitations are not available right now."),
        };
    }

    public static async Task<IResult> Shares(string providerId, int viewId, HttpContext http, ProviderAccess access, ISavedViewInvitationStore store, CancellationToken cancellationToken)
    {
        if (await ActorAsync(http, providerId, access, cancellationToken) is not { } actor
            || await store.ListSharingAsync(actor, viewId, cancellationToken) is not { } sharing)
        {
            return Results.NotFound();
        }

        return Results.Ok(new SharingResponse(
            [.. sharing.Recipients.Select(r => new RecipientResponse(r.ShareId, r.Email, r.SharedAt))],
            [.. sharing.Pending.Select(p => new PendingInvitationResponse(p.InvitationId, p.Email, p.ExpiresAt, p.IsExpired))]));
    }

    public static async Task<IResult> RevokeShare(string providerId, int viewId, int shareId, HttpContext http, ProviderAccess access, ISavedViewInvitationStore store, CancellationToken cancellationToken) =>
        await ActorAsync(http, providerId, access, cancellationToken) is { } actor && await store.RevokeShareAsync(actor, viewId, shareId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    public static async Task<IResult> RevokeInvitation(string providerId, int viewId, int invitationId, HttpContext http, ProviderAccess access, ISavedViewInvitationStore store, CancellationToken cancellationToken) =>
        await ActorAsync(http, providerId, access, cancellationToken) is { } actor && await store.RevokeInvitationAsync(actor, viewId, invitationId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    public static async Task<IResult> Redeem(RedeemRequest? body, HttpContext http, ProviderAccess access, ISavedViewInvitationStore store, CancellationToken cancellationToken)
    {
        // One answer for every failure, and the cheap shape check comes before any database work.
        if (!InvitationTokens.IsWellFormed(body?.Token)
            || !long.TryParse(http.User.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId)
            || await store.FindTargetAsync(body!.Token!, cancellationToken) is not { } target
            || await access.ResolveAsync(http.User, target.ProviderId.ToString(CultureInfo.InvariantCulture), cancellationToken) != target.ProviderId
            || await store.RedeemAsync(body.Token!, portalUserId, cancellationToken) is not { } redeemed)
        {
            return Results.NotFound();
        }

        return Results.Ok(new RedeemResponse(redeemed.ProviderId.ToString(CultureInfo.InvariantCulture), redeemed.SavedViewId));
    }

    private static string ETag(uint version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static SavedViewContent Content(SavedViewRequest body) =>
        new(body.Name!.Trim(), string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(), body.Layout!);

    /// <summary>A 400 naming the fields and codes at fault, never the values sent. Null when the body is acceptable.</summary>
    private static IResult? Validate(SavedViewRequest? body) => ValidateContent(body?.Name, body?.Description, body?.Layout);

    /// <summary>The same name, description and layout rules for a Saved View and a View Template. Codes only, never the values sent.</summary>
    internal static IResult? ValidateContent(string? rawName, string? rawDescription, SavedViewLayout? layout)
    {
        Dictionary<string, string[]> errors = [];
        string name = rawName?.Trim() ?? "";
        if (name.Length is 0 or > SavedViewLimits.MaxNameLength || name.Any(char.IsControl))
        {
            errors["name"] = ["invalid_name"];
        }

        if (rawDescription is { } description && (description.Length > SavedViewLimits.MaxDescriptionLength || description.Any(c => char.IsControl(c) && c is not '\n' and not '\r')))
        {
            errors["description"] = ["invalid_description"];
        }

        IReadOnlyList<string> layoutProblems = LayoutValidator.Validate(layout);
        if (layoutProblems.Count == 0 && JsonSerializer.SerializeToUtf8Bytes(layout, LayoutJson).Length > SavedViewLimits.MaxLayoutBytes)
        {
            layoutProblems = ["layout_too_large"];
        }

        if (layoutProblems.Count > 0)
        {
            errors["layout"] = [.. layoutProblems];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static IResult WriteProblem(SavedViewWriteStatus status) => status switch
    {
        SavedViewWriteStatus.NameTaken => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You already have a view with this name."),
        SavedViewWriteStatus.LimitReached => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You have reached the number of views one person can keep here."),
        SavedViewWriteStatus.Conflict => Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "This view was saved by someone else since you opened it."),
        _ => Results.NotFound(),
    };

    private static List<string>? ParseEmails(InviteRequest? body)
    {
        if (body?.Emails is not { Count: > 0 and <= SavedViewLimits.MaxEmailsPerRequest } raw)
        {
            return null;
        }

        List<string> emails = [];
        foreach (string? address in raw)
        {
            if (InvitationEmails.Normalize(address) is not { } normalized)
            {
                return null;
            }

            emails.Add(normalized);
        }

        return emails;
    }

    private static SummaryResponse Summary(SavedViewSummary s) => new(s.Id, s.Name, s.Description, s.OwnerName, s.IsOwner, s.WidgetCount, s.UpdatedAt) { FromTemplate = s.FromTemplate, IsHidden = s.IsHidden };

    private static SavedViewResponse Detail(SavedViewDetail v) => new(v.Id, v.Name, v.Description, v.OwnerName, v.IsOwner, v.Layout, v.Version.ToString(CultureInfo.InvariantCulture), v.UpdatedAt);
}

/// <summary>Sets the body size limit for an endpoint. A larger body is refused before it is read.</summary>
internal sealed class RequestSizeLimitMetadata(long maxBytes) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = maxBytes;
}

/// <summary>A view to save. Only these three fields are read: anything else in the body, such as an owner or Provider, is ignored.</summary>
public sealed record SavedViewRequest(string? Name, string? Description, SavedViewLayout? Layout);

/// <summary>The addresses to invite, at most 10.</summary>
public sealed record InviteRequest(IReadOnlyList<string>? Emails);

/// <summary>The token from an emailed link.</summary>
public sealed record RedeemRequest(string? Token);

public sealed record RedeemResponse(string ProviderId, int ViewId);

public sealed record SummaryResponse(int Id, string Name, string? Description, string? OwnerName, bool IsOwner, int WidgetCount, DateTimeOffset UpdatedAt)
{
    /// <summary>True when the view arrived by a push from the Prism team, so the SPA can mark it.</summary>
    public bool FromTemplate { get; init; }

    /// <summary>True when the owner hid it. Only the settings list shows hidden views.</summary>
    public bool IsHidden { get; init; }
}

public sealed record SavedViewListResponse(IReadOnlyList<SummaryResponse> Mine, IReadOnlyList<SummaryResponse> Shared);

/// <summary>A view with its layout. <c>Version</c> is the same value as the <c>ETag</c> header, without the quotes.</summary>
public sealed record SavedViewResponse(int Id, string Name, string? Description, string? OwnerName, bool IsOwner, SavedViewLayout Layout, string Version, DateTimeOffset UpdatedAt);

public sealed record PendingInvitationResponse(int InvitationId, string Email, DateTimeOffset ExpiresAt, bool IsExpired);

public sealed record RecipientResponse(int ShareId, string? Email, DateTimeOffset SharedAt);

public sealed record SharingResponse(IReadOnlyList<RecipientResponse> Recipients, IReadOnlyList<PendingInvitationResponse> Pending);

public sealed record InviteResponse(IReadOnlyList<PendingInvitationResponse> Sent, IReadOnlyList<string> Failed);
