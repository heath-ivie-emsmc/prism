using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Net cash collected for a Provider: the periods that have data, and a run of months ending at one of them.</summary>
internal static class NetCollectionsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", Series)
            .WithName("GetNetCollections")
            .WithSummary("Net cash collected by month")
            .WithDescription(
                "Net cash collected for the Provider in each of the last `months` accounting periods ending with `endPeriod`, from the latest warehouse load. "
                + "`endPeriod` is `yyyyMM` and defaults to the newest period that has data. `months` defaults to 6 and is at most 24. "
                + "`serviceLine` is `all` (default, the Provider's whole figure), `air` or `ground`: air or ground alone is added up from the payments ledger by the transport's level of service, "
                + "because the warehouse's summary table has no such split. "
                + "Months with no data are left out, not returned as zero. The figures are aggregates: no patient or claim detail. "
                + "Returns 404 when the Provider is not one the caller may read in full (not assigned, or limited to some Facilities).")
            .Produces<NetCollectionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    /// <summary>The Period drop-down's route. Mapped by its own module so it keeps its place after every Metric route.</summary>
    public static void MapPeriods(RouteGroupBuilder group)
    {
        group.MapGet("/periods", Periods)
            .WithName("GetNetCollectionsPeriods")
            .WithSummary("Accounting periods with net cash data")
            .WithDescription("The accounting periods the Provider has net cash data for, newest first, as `yyyyMM` with a display label. Fills the Period drop-down.")
            .Produces<NetCollectionsPeriodsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> Periods(
        string providerId, ClaimsPrincipal user, ProviderAccess access, INetCollectionsMetric metric, CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        IReadOnlyList<ApPeriod> periods = await metric.GetPeriodsAsync(id, cancellationToken);
        return Results.Ok(new NetCollectionsPeriodsResponse(providerId, periods.Select(Describe).ToArray()));
    }

    public static async Task<IResult> Series(
        [AsParameters] SeriesRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months, string? serviceLine) = (request.ProviderId, request.EndPeriod, request.Months, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        int count = months ?? INetCollectionsMetric.DefaultMonths;
        if (count is < 1 or > INetCollectionsMetric.MaxMonths)
        {
            return Invalid("months", $"months must be between 1 and {INetCollectionsMetric.MaxMonths}.");
        }

        if (!TryParseServiceLine(serviceLine, out ServiceLineFilter filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        string? through = await access.DataThroughAsync(id, INetCollectionsMetric.Definition.Key, filter, cancellationToken);
        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await metric.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(Empty(providerId, count) with { DataThrough = through });
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        NetCollectionsSeries series = await metric.GetSeriesAsync(id, end, count, filter, cancellationToken);
        NetCollectionsResponse response = new(
            new MetricResponse(series.Metric.Key, series.Metric.Name, series.Metric.Description, series.Metric.Unit),
            providerId,
            series.EndPeriod.ToString(),
            series.Months,
            series.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            series.Points.Select(p => new NetCollectionsPointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.Value)).ToArray());
        return Results.Ok(response with { DataThrough = through });
    }

    private static PeriodResponse Describe(ApPeriod period) =>
        new(period.ToString(), $"{Months[period.Month - 1]} {period.Year}");

    private static NetCollectionsResponse Empty(string providerId, int months) =>
        new(
            new MetricResponse(INetCollectionsMetric.Definition.Key, INetCollectionsMetric.Definition.Name, INetCollectionsMetric.Definition.Description, INetCollectionsMetric.Definition.Unit),
            providerId,
            null,
            months,
            null,
            []);
}

/// <summary>Net cash collected for a Provider, by accounting period.</summary>
public sealed record NetCollectionsResponse(
    MetricResponse Metric,
    string ProviderId,
    string? EndPeriod,
    int Months,
    string? SnapshotDate,
    IReadOnlyList<NetCollectionsPointResponse> Points)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }
}


/// <summary>One month: <c>Period</c> is <c>yyyyMM</c>, <c>Label</c> the short month name, <c>Value</c> the amount in the Metric's unit.</summary>
public sealed record NetCollectionsPointResponse(string Period, string Label, int Year, decimal Value);

public sealed record NetCollectionsPeriodsResponse(string ProviderId, IReadOnlyList<PeriodResponse> Periods);


/// <summary>Route and query values of the net cash series route.</summary>
internal readonly record struct SeriesRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months,
    string? ServiceLine);
