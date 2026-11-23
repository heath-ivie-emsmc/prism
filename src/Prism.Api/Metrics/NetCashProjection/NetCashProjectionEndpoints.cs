using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Projected net cash for the months after a period.</summary>
internal static class NetCashProjectionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/projection", Projection)
            .WithName("GetNetCashProjection")
            .WithSummary("Projected net cash for the next three months")
            .WithDescription(
                "The cash the warehouse forecasts for up to three months after `endPeriod` (`yyyyMM`, default the newest period with net cash data), from the latest forecast load. "
                + "`serviceLine` is `all` (default), `air` or `ground`. Each month has the projected `value` and a likely range (`low` to `high`), the lower and higher of the AR-based and budget-based forecasts. "
                + "`total`, `low` and `high` are the sums over the returned months. Fewer than three months, or none, come back when the forecast is shorter. Aggregates only. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashProjectionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> Projection(
        [AsParameters] ProjectionRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashProjectionMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, string? serviceLine) = (request.ProviderId, request.EndPeriod, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        if (!TryParseServiceLine(serviceLine, out ServiceLineFilter filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        string? through = await access.DataThroughAsync(id, INetCashProjectionMetric.Definition.Key, filter, cancellationToken);
        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(EmptyProjection(providerId, filter) with { DataThrough = through });
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        NetCashProjection projection = await metric.GetProjectionAsync(id, end, filter, cancellationToken);
        NetCashProjectionResponse response = new(
            new MetricResponse(projection.Metric.Key, projection.Metric.Name, projection.Metric.Description, projection.Metric.Unit),
            providerId,
            projection.AfterPeriod.ToString(),
            ServiceLineName(filter),
            projection.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            projection.Total,
            projection.Low,
            projection.High,
            projection.Months.Select(m => new NetCashProjectionMonthResponse(m.Period.ToString(), Months[m.Period.Month - 1], m.Period.Year, m.Value, m.Low, m.High)).ToArray());
        return Results.Ok(response with { DataThrough = through });
    }

    private static NetCashProjectionResponse EmptyProjection(string providerId, ServiceLineFilter filter) =>
        new(
            new MetricResponse(INetCashProjectionMetric.Definition.Key, INetCashProjectionMetric.Definition.Name, INetCashProjectionMetric.Definition.Description, INetCashProjectionMetric.Definition.Unit),
            providerId,
            null,
            ServiceLineName(filter),
            null,
            0m,
            0m,
            0m,
            []);
}

/// <summary>Route and query values of the projection route, bound together to keep the handler's parameter list short.</summary>
internal readonly record struct ProjectionRequest(
    string ProviderId,
    string? EndPeriod,
    string? ServiceLine);

/// <summary>The cash forecast for the months after <c>AfterPeriod</c>. <c>Total</c>, <c>Low</c> and <c>High</c> are sums over <c>Months</c>.</summary>
public sealed record NetCashProjectionResponse(
    MetricResponse Metric,
    string ProviderId,
    string? AfterPeriod,
    string ServiceLine,
    string? SnapshotDate,
    decimal Total,
    decimal Low,
    decimal High,
    IReadOnlyList<NetCashProjectionMonthResponse> Months)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }
}

/// <summary>One projected month: <c>Period</c> is <c>yyyyMM</c>, <c>Value</c> the projected cash, <c>Low</c> to <c>High</c> the likely range.</summary>
public sealed record NetCashProjectionMonthResponse(string Period, string Label, int Year, decimal Value, decimal Low, decimal High);
