using System.Globalization;
using System.Security.Claims;
using System.Text;
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

    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/claims/attention")
            .RequireAuthorization(Permissions.ClaimsView)
            .WithTags("Claims needing attention");

        group.MapGet("/counts", Counts)
            .WithName("GetAttentionCounts")
            .WithSummary("How many claims each Attention Check flags")
            .WithDescription(
                "For `period` (`yyyyMM`, required) and `serviceLine` (`all`, `air` or `ground`, default `all`) and optionally `group` (an aircraft type or level of service key from the breakdown route, such as `fixed-wing`), the number of claims flagged by each Attention Check "
                + "(`denied`, `needs_attention`, `all`), by date of service in the latest warehouse load, and the balance still owed on the denied claims. Aggregates only, but it needs the claims permission. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<AttentionCountsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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
            counts.DataThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            counts.Counts.Select(c => new AttentionCountResponse(c.Key, AttentionCatalog.Find(c.Key)!.Name, c.Count)).ToArray(),
            counts.DeniedBalanceDue));
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

        AttentionClaims result;
        try
        {
            result = await claims.GetClaimsAsync(id, check, scope.Period, scope.ServiceLine, group, cancellationToken);
        }
        catch (WarehouseResultTooLargeException)
        {
            return TooMany();
        }

        AttentionClaimResponse[] rows = result.Claims.Skip((number - 1) * size).Take(size).Select(ToResponse).ToArray();
        if (!await TryAuditAsync(user, audit, id, new AuditRead(AuditActions.ClaimsList, check.Key, scope.Period, rows.Length), cancellationToken))
        {
            return Results.NotFound();
        }

        return Results.Ok(new AttentionClaimsResponse(
            providerId, check.Key, scope.Period.ToString(), result.DataThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result.Claims.Count, number, size, rows));
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

        AttentionClaims result;
        try
        {
            result = await claims.GetClaimsAsync(id, check, scope.Period, scope.ServiceLine, group, cancellationToken);
        }
        catch (WarehouseResultTooLargeException)
        {
            return TooMany();
        }

        if (!await TryAuditAsync(user, audit, id, new AuditRead(AuditActions.ClaimsExport, check.Key, scope.Period, result.Claims.Count), cancellationToken))
        {
            return Results.NotFound();
        }

        string csv = CsvWriter.Write(CsvHeader, result.Claims.Select(CsvRow));
        string fileName = $"prism-needs-attention-{check.Key.Replace('_', '-')}-{time.GetUtcNow():yyyy-MM-dd}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    private static readonly string[] CsvHeader =
    [
        "Claim", "Service date", "Payer", "Payer category", "Vehicle", "Reporting division", "Status", "Denial code", "Denial reason",
        "Balance due", "Estimated filing due date", "Filing days left",
    ];

    private static string[] CsvRow(AttentionClaim c) =>
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
        c.BalanceDue.ToString("0.00", CultureInfo.InvariantCulture),
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
        && NetCollectionsEndpoints.TryParseServiceLine(serviceLine, out ServiceLineFilter filter)
        && (group is null || NetCollectionsEndpoints.GroupKeyPattern().IsMatch(group))
            ? (parsed, filter)
            : null;

    private static IResult Invalid(string? period, string? group)
    {
        if (period is null || !ApPeriod.TryParse(period, out _))
        {
            return NetCollectionsEndpoints.Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        return group is not null && !NetCollectionsEndpoints.GroupKeyPattern().IsMatch(group)
            ? NetCollectionsEndpoints.Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.")
            : NetCollectionsEndpoints.Invalid("serviceLine", "serviceLine must be all, air or ground.");
    }

    private static IResult TooMany() =>
        Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Too many claims",
            detail: "More than 10,000 claims match. Narrow the period or Service Line.");

    // The audit row is written before the response leaves. A failed write throws and the request fails, so no read goes unrecorded.
    private static async Task<bool> TryAuditAsync(
        ClaimsPrincipal user, IAuditLog audit, int providerId, AuditRead read, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId))
        {
            return false;
        }

        await audit.WriteAsync(providerId, portalUserId, read.Action, read.CheckKey, read.Period.ToString(), read.Rows, cancellationToken);
        return true;
    }
}

/// <summary>What one audited read touched: the kind, the check, the period and how many rows.</summary>
internal sealed record AuditRead(string Action, string CheckKey, ApPeriod Period, int Rows);

/// <summary>The route and query values of the claims list.</summary>
internal sealed record ClaimsRequest(string ProviderId, string CheckKey, string? Period, string? ServiceLine, string? Group, int? Page, int? PageSize);

/// <summary>The route and query values of the claims export.</summary>
internal sealed record ExportRequest(string ProviderId, string CheckKey, string? Period, string? ServiceLine, string? Group);

/// <summary>The route and query values of the counts.</summary>
internal sealed record CountsRequest(string ProviderId, string? Period, string? ServiceLine, string? Group);

/// <summary>The number of claims each Attention Check flags in a period. Aggregates only.</summary>
public sealed record AttentionCountsResponse(string ProviderId, string Period, string? DataThrough, IReadOnlyList<AttentionCountResponse> Counts, decimal DeniedBalanceDue);

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
