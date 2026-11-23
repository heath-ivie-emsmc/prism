using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Clean claim rate by month.</summary>
internal static class CleanClaimRateEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/clean-claims", CleanClaimsRoute)
            .WithName("GetCleanClaimRate")
            .WithSummary("Clean claim rate by month")
            .WithDescription(
                "The share of claims accepted first time, by month of service, for each of the last `months` months up to `endPeriod` (`yyyyMM`, default the newest period with net cash data) that the warehouse has a figure for. "
                + "`months` defaults to 6 and is at most 24. `rate` is 0 to 1, or null when there were no claims. Months the warehouse has no figure for are left out, so the newest point can be older than `endPeriod`: "
                + "the warehouse table is refreshed on its own schedule. The figure covers all of the Provider's claims, not one Service Line. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<CleanClaimRateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> CleanClaimsRoute(
        [AsParameters] CleanClaimsRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ICleanClaimRateMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months) = (request.ProviderId, request.EndPeriod, request.Months);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        int count = months ?? INetCollectionsMetric.DefaultMonths;
        if (count is < 1 or > ICleanClaimRateMetric.MaxMonths)
        {
            return Invalid("months", $"months must be between 1 and {ICleanClaimRateMetric.MaxMonths}.");
        }

        string? through = await access.DataThroughAsync(id, ICleanClaimRateMetric.Definition.Key, ServiceLineFilter.Both, cancellationToken);
        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new CleanClaimRateResponse(MetricOf(ICleanClaimRateMetric.Definition), providerId, null, count, null, []) with { DataThrough = through });
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        CleanClaimRate rate = await metric.GetRateAsync(id, end, count, cancellationToken);
        CleanClaimRateResponse response = new(
            MetricOf(rate.Metric),
            providerId,
            rate.EndPeriod.ToString(),
            rate.Months,
            rate.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rate.Points.Select(p => new CleanClaimPointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.CleanClaims, p.RejectedClaims, p.TotalClaims, p.Rate)).ToArray());
        return Results.Ok(response with { DataThrough = through });
    }
}

/// <summary>Route and query values of the clean claims route.</summary>
internal readonly record struct CleanClaimsRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months);

/// <summary>
/// The clean claim rate by month of service up to <c>EndPeriod</c>. <c>Points</c> only has months the warehouse has a figure for, so the newest can be older than
/// <c>EndPeriod</c>. <c>SnapshotDate</c> is the warehouse load the figures came from.
/// </summary>
public sealed record CleanClaimRateResponse(
    MetricResponse Metric,
    string ProviderId,
    string? EndPeriod,
    int Months,
    string? SnapshotDate,
    IReadOnlyList<CleanClaimPointResponse> Points)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }
}

/// <summary>One month of service: claims accepted first time, rejected, the total, and <c>Rate</c> (0 to 1, null when there were no claims).</summary>
public sealed record CleanClaimPointResponse(string Period, string Label, int Year, int CleanClaims, int RejectedClaims, int TotalClaims, decimal? Rate);
