using System.Globalization;
using System.IO.Pipelines;
using System.Security.Claims;
using Prism.Api.Metrics;
using Prism.Application;
using Prism.Application.Attention;
using Prism.Application.Audit;
using Prism.Application.Auth;
using Prism.Application.Export;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Attention;

/// <summary>
/// The claims that need a customer's attention, by Attention Check. Claim rows are PHI: they need the claims permission and leave an audit record.
/// The counts are aggregates but sit behind the same permission, so a user without it cannot learn how many claims are flagged.
/// </summary>
internal static class ClaimAttentionEndpoints
{
    internal const int MaxPageSize = 100;
    internal const int DefaultPageSize = 25;
    internal const int MaxPage = 1000;
    internal const int MaxGroupKeyLength = 40;
    public const string RateLimitPolicy = "claims-attention";

    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/claims/attention")
            .RequireAuthorization(Permissions.ClaimsView)
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter(NoStore)
            .AddEndpointFilter(PerUserLimit)
            .WithTags("Claims needing attention");

        group.MapGet("/counts", Counts)
            .WithName("GetAttentionCounts")
            .WithSummary("How many claims each Attention Check flags")
            .WithDescription(
                "For `period` (`yyyyMM`, required) and `serviceLine` (`all`, `air` or `ground`, default `all`) and optionally `group` (an aircraft type or level of service key from the breakdown route, such as `fixed-wing`), the number of claims flagged by each Attention Check "
                + "(`denied`, `needs_attention`, `all`), by date of service in the latest warehouse load, and the balance still owed on the denied claims. Aggregates only, but it needs the claims permission. "
                + "It also returns `deniedRate` (denied claims divided by all claims in the period, 0 to 1, null when there are none); `reasons` (the top 10 denial reasons by denied claim count, each with `count` and `balanceDue`, then one `isOther` row for the rest; "
                + "reason text is cleaned in SQL, cut to 120 characters, and empty is `No reason given`); `categories` (separate counts and balance due for Denied, On hold and In collections, which can overlap, so do not add them); "
                + "and `filingBands` (needs-attention claims and balance due that are past due, due within 10 days, within 30 days, and later, from the derived filing deadline and Data Through; the filing limits are a placeholder until the real payer limits are supplied, so the bands are estimates). "
                + "There is no assigned owner, and no IDR, patient-due or missing-documentation category: the warehouse has no source for them. The response is never cached. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<AttentionCountsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{checkKey}", Claims)
            .WithName("GetAttentionClaims")
            .WithSummary("The claims one Attention Check flags")
            .WithDescription(
                "The claims flagged by `checkKey` (`denied`, `needs_attention` or `all`) for `period` (`yyyyMM`, required), `serviceLine` and optionally `group`, soonest estimated filing deadline first. "
                + "`page` starts at 1 and `pageSize` is at most 100. `balanceDue` is what is still owed, not an estimate of what will be paid. "
                + "`filingDueDate` is an estimate: the date of service plus a filing limit for the payer category, and `daysLeft` counts from `dataThrough`. "
                + "Claim data is PHI: it needs the claims permission and the read is audited. More than 10,000 matching claims returns 422. Returns 404 for an unknown Provider or check.")
            .Produces<AttentionClaimsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{checkKey}/export", Export)
            .WithName("ExportAttentionClaims")
            .WithSummary("Download the claims one Attention Check flags as CSV")
            .WithDescription(
                "The same claims, in the same order, as the list route, as a CSV file with every cell quoted and spreadsheet formulas neutralized. "
                + "The file name is built from the check and the date only. Claim data is PHI: it needs the claims permission and the download is audited. "
                + "More than 10,000 matching claims returns 422.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    // Claim data must never sit in a browser or proxy cache.
    private static ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return next(context);
    }

    // Runs after authentication, so the budget is the signed-in user's, not the address's. A session without a user id is left to the handlers (404).
    private static ValueTask<object?> PerUserLimit(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (PortalUserId(http.User) is { } userId && !http.RequestServices.GetRequiredService<ClaimReadLimiter>().TryAcquire(userId))
        {
            http.Response.Headers.RetryAfter = "60";
            return ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests"));
        }

        return next(context);
    }

    public static async Task<IResult> Counts(
        [AsParameters] CountsRequest request, ClaimsPrincipal user, ProviderAccess access, IClaimAttention claims, CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? group) = (request.ProviderId, request.Period, request.ServiceLine, request.Group);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        if (Validate(period, serviceLine, group) is not { } scope)
        {
            return Invalid(period, group);
        }

        AttentionCounts counts = await claims.GetCountsAsync(id, scope.Period, scope.ServiceLine, group, cancellationToken);
        return Results.Ok(new AttentionCountsResponse(
            providerId,
            scope.Period.ToString(),
            await access.DataThroughAsync(id, MetricDataThrough.ClaimAttention, scope.ServiceLine, cancellationToken),
            counts.Counts.Select(c => new AttentionCountResponse(c.Key, AttentionCatalog.Find(c.Key)!.Name, c.Count)).ToArray(),
            counts.DeniedBalanceDue)
        {
            DeniedRate = counts.DeniedRate,
            Reasons = (counts.Reasons ?? []).Select(r => new DenialReasonResponse(r.Reason, r.IsOther, r.Count, r.BalanceDue)).ToArray(),
            Categories = (counts.Categories ?? []).Select(c => new AttentionCategoryResponse(c.Key, c.Name, c.Count, c.BalanceDue)).ToArray(),
            FilingBands = (counts.Bands ?? []).Select(b => new FilingBandResponse(b.Key, b.Name, b.Claims, b.BalanceDue)).ToArray(),
        });
    }

    public static async Task<IResult> Claims(
        [AsParameters] ClaimsRequest request, ClaimsPrincipal user, ProviderAccess access, IClaimAttention claims, IAuditLog audit, CancellationToken cancellationToken)
    {
        (string providerId, string checkKey, string? period, string? serviceLine, string? group) = (request.ProviderId, request.CheckKey, request.Period, request.ServiceLine, request.Group);
        (int? page, int? pageSize) = (request.Page, request.PageSize);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id || AttentionCatalog.Find(checkKey) is not { } check)
        {
            return Results.NotFound();
        }

        int size = pageSize ?? DefaultPageSize;
        int number = page ?? 1;
        if (Validate(period, serviceLine, group) is not { } scope || size is < 1 or > MaxPageSize || number is < 1 or > MaxPage)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [$"period must be yyyyMM, serviceLine must be all, air or ground, pageSize must be 1 to {MaxPageSize} and page 1 to {MaxPage}."],
            });
        }

        // The audit identity is checked before the PHI query runs, so a session without one never reads a claim.
        if (PortalUserId(user) is not { } portalUserId)
        {
            return Results.NotFound();
        }

        AttentionClaims result;
        try
        {
            result = await claims.GetClaimsAsync(id, check, scope.Period, scope.ServiceLine, group, new AttentionPage(number, size), cancellationToken);
        }
        catch (WarehouseResultTooLargeException)
        {
            return TooMany();
        }

        AttentionClaimResponse[] rows = result.Claims.Select(ToResponse).ToArray();
        await audit.WriteAsync(id, portalUserId, new AuditRead(AuditActions.ClaimsList, check.Key, scope.Period, rows.Length, group), cancellationToken);

        return Results.Ok(new AttentionClaimsResponse(
            providerId, check.Key, scope.Period.ToString(), await access.DataThroughAsync(id, MetricDataThrough.ClaimAttention, scope.ServiceLine, cancellationToken), result.Total, number, size, rows));
    }

    public static async Task<IResult> Export(
        [AsParameters] ExportRequest request, ClaimsPrincipal user, ProviderAccess access, IClaimAttention claims, IAuditLog audit, TimeProvider time, CancellationToken cancellationToken)
    {
        (string providerId, string checkKey, string? period, string? serviceLine, string? group) = (request.ProviderId, request.CheckKey, request.Period, request.ServiceLine, request.Group);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id || AttentionCatalog.Find(checkKey) is not { } check)
        {
            return Results.NotFound();
        }

        if (Validate(period, serviceLine, group) is not { } scope)
        {
            return Invalid(period, group);
        }

        // The audit identity is checked before the PHI query runs, so a session without one never reads a claim.
        if (PortalUserId(user) is not { } portalUserId)
        {
            return Results.NotFound();
        }

        AttentionClaims result;
        try
        {
            result = await claims.GetClaimsAsync(id, check, scope.Period, scope.ServiceLine, group, null, cancellationToken);
        }
        catch (WarehouseResultTooLargeException)
        {
            return TooMany();
        }

        await audit.WriteAsync(id, portalUserId, new AuditRead(AuditActions.ClaimsExport, check.Key, scope.Period, result.Claims.Count, group), cancellationToken);

        string fileName = $"prism-needs-attention-{check.Key.Replace('_', '-')}-{time.GetUtcNow():yyyy-MM-dd}.csv";
        IReadOnlyList<AttentionClaim> exported = result.Claims;
        return Results.Stream(
            async stream =>
            {
                // UTF-8 goes straight to the response in pipe-sized chunks: no whole-file string, no second byte copy.
                var output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
                CsvWriter.Write(output, CsvHeader, exported.Select(CsvRow));
                await output.CompleteAsync();
            },
            "text/csv; charset=utf-8",
            fileName);
    }

    private static readonly string[] CsvHeader =
    [
        "Claim", "Service date", "Payer", "Payer category", "Vehicle", "Reporting division", "Status", "Denial code", "Denial reason",
        "Balance due", "Estimated filing due date", "Filing days left",
    ];

    private static CsvCell[] CsvRow(AttentionClaim c) =>
    [
        c.ClaimRef,
        c.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.Payer ?? "",
        c.PayerCategory ?? "",
        c.Vehicle ?? "",
        c.ReportingDivision,
        c.Status,
        c.DenialCode ?? "",
        c.DenialReason ?? "",
        CsvCell.Number(c.BalanceDue),
        c.FilingDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.DaysLeft >= 0 ? c.DaysLeft.ToString(CultureInfo.InvariantCulture) : $"Overdue by {-c.DaysLeft}",
    ];

    private static AttentionClaimResponse ToResponse(AttentionClaim c) => new(
        c.EncounterId.ToString(CultureInfo.InvariantCulture),
        c.ClaimRef,
        c.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.Payer,
        c.PayerCategory,
        c.Vehicle,
        c.ReportingDivision,
        c.Status,
        c.DenialCode,
        c.DenialReason,
        c.BalanceDue,
        c.FilingDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.DaysLeft);

    private static (ApPeriod Period, ServiceLineFilter ServiceLine)? Validate(string? period, string? serviceLine, string? group) =>
        period is not null && ApPeriod.TryParse(period, out ApPeriod parsed)
        && MetricEndpointSupport.TryParseServiceLine(serviceLine, out ServiceLineFilter filter)
        && IsGroupKey(group)
            ? (parsed, filter)
            : null;

    // Length is capped before the pattern runs. The pattern is anchored at the very end (not before a trailing newline).
    private static bool IsGroupKey(string? group) =>
        group is null || (group.Length <= MaxGroupKeyLength && MetricEndpointSupport.GroupKeyPattern().IsMatch(group));

    private static IResult Invalid(string? period, string? group)
    {
        if (period is null || !ApPeriod.TryParse(period, out _))
        {
            return MetricEndpointSupport.Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        return !IsGroupKey(group)
            ? MetricEndpointSupport.Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.")
            : MetricEndpointSupport.Invalid("serviceLine", "serviceLine must be all, air or ground.");
    }

    private static IResult TooMany() =>
        Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Too many claims",
            detail: "More than 10,000 claims match. Narrow the period or Service Line.");

    private static long? PortalUserId(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long id) ? id : null;
}

/// <summary>The route and query values of the claims list.</summary>
internal sealed record ClaimsRequest(string ProviderId, string CheckKey, string? Period, string? ServiceLine, string? Group, int? Page, int? PageSize);

/// <summary>The route and query values of the claims export.</summary>
internal sealed record ExportRequest(string ProviderId, string CheckKey, string? Period, string? ServiceLine, string? Group);

/// <summary>The route and query values of the counts.</summary>
internal sealed record CountsRequest(string ProviderId, string? Period, string? ServiceLine, string? Group);

/// <summary>The number of claims each Attention Check flags in a period, with the denial rate, reasons, categories and filing bands. Aggregates only.</summary>
public sealed record AttentionCountsResponse(string ProviderId, string Period, string? DataThrough, IReadOnlyList<AttentionCountResponse> Counts, decimal DeniedBalanceDue)
{
    /// <summary>Denied claims divided by all claims in the period, 0 to 1 (four decimals). Null when the period has no claims.</summary>
    public decimal? DeniedRate { get; init; }

    /// <summary>The top 10 denial reasons by denied claim count, then one <c>isOther</c> row for the rest. Text is cleaned and at most 120 characters.</summary>
    public IReadOnlyList<DenialReasonResponse> Reasons { get; init; } = [];

    /// <summary>Denied, On hold and In collections, each with its claim count and balance due. A claim can be in more than one, so they overlap and must not be added.</summary>
    public IReadOnlyList<AttentionCategoryResponse> Categories { get; init; } = [];

    /// <summary>Needs-attention claims by estimated filing deadline: past due, within 10 days, 11 to 30 days, later. Always four rows, in that order.</summary>
    public IReadOnlyList<FilingBandResponse> FilingBands { get; init; } = [];
}

/// <summary>A denial reason (<c>Reason</c>, cleaned warehouse text, or <c>No reason given</c>), the denied claims behind it and their <c>BalanceDue</c> (USD). <c>IsOther</c> is the row that sums the reasons beyond the top 10.</summary>
public sealed record DenialReasonResponse(string Reason, bool IsOther, int Count, decimal BalanceDue);

/// <summary>A category of claim needing action: <c>Key</c> is <c>denied</c>, <c>on_hold</c> or <c>in_collections</c>. <c>BalanceDue</c> (USD) adds only positive balances.</summary>
public sealed record AttentionCategoryResponse(string Key, string Name, int Count, decimal BalanceDue);

/// <summary>A filing band: <c>Key</c> is <c>past_due</c>, <c>due_within_10_days</c>, <c>due_within_30_days</c> or <c>due_later</c>. <c>Claims</c> and <c>BalanceDue</c> (USD) are for needs-attention claims.</summary>
public sealed record FilingBandResponse(string Key, string Name, int Claims, decimal BalanceDue);

/// <summary>One check and how many claims it flags.</summary>
public sealed record AttentionCountResponse(string Key, string Name, int Count);

/// <summary>One page of the claims a check flags.</summary>
public sealed record AttentionClaimsResponse(
    string ProviderId, string Check, string Period, string? DataThrough, int Total, int Page, int PageSize, IReadOnlyList<AttentionClaimResponse> Claims);

/// <summary>One flagged claim. Every field is PHI: it can identify a patient's claim.</summary>
public sealed record AttentionClaimResponse(
    [property: Phi] string Id,
    [property: Phi] string ClaimRef,
    [property: Phi] string ServiceDate,
    [property: Phi] string? Payer,
    [property: Phi] string? PayerCategory,
    [property: Phi] string? Vehicle,
    [property: Phi] string ReportingDivision,
    [property: Phi] string Status,
    [property: Phi] string? DenialCode,
    [property: Phi] string? DenialReason,
    [property: Phi] decimal BalanceDue,
    [property: Phi] string FilingDueDate,
    [property: Phi] int DaysLeft);
