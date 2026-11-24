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
            .WithSummary("Projected net cash for the next one to three months")
            .WithDescription(
                "The cash the warehouse forecasts for up to three months after `endPeriod` (`yyyyMM`, default the newest period with net cash data), from the latest forecast load. "
                + "`serviceLine` is `all` (default), `air` or `ground`. `horizon` (1 to 3, default 3) is how many months to return: the forecast table holds only the next three months, so anything outside 1 to 3 is a 400. "
                + "Each month also reports the two components the warehouse forecasts, as read: `fromExistingTrips` (expected collections on trips that already existed) and `fromFutureTrips` (assumed run rate of trips that do not exist yet), with `existingTripsShare` and `futureTripsShare` (0 to 1, null when the month's value is zero or the component is missing). They are not assumed to add up to `value`, and a trip that already existed may not be billed yet. "
                + "Service Line is the only filter: the forecast table has no Reporting Division, call type, aircraft or payer column, so none is offered. "
                + "Each month has the projected `value` and a likely range (`low` to `high`): the lower and higher of the total and the two components. That range is a product rule (the lower and higher of the total and the two components), not a statistical interval or confidence band. "
                + "Each month also has `changeFromPrevious` (`amount` in USD and `percent`, null when the base is zero) and a running `cumulative` (`value`, `low`, `high`) from the first returned month. The first month is compared with `latestActual`, "
                + "the latest month with posted net cash within the six months up to `endPeriod`; `changeFromPrevious` is null on the first month, and `latestActual` is null, when there is none. "
                + "`horizon` totals the returned months (`value`, `low`, `high`, `months`), and `total`, `low` and `high` are the same sums. Fewer months than asked for, or none, come back when the forecast is shorter. Aggregates only. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashProjectionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    private const int ActualLookbackMonths = 6;

    public static async Task<IResult> Projection(
        [AsParameters] ProjectionRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashProjectionMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, string? serviceLine, int? horizonValue) = (request.ProviderId, request.EndPeriod, request.ServiceLine, request.Horizon);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        if (!TryParseServiceLine(serviceLine, out ServiceLineFilter filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        int horizon = horizonValue ?? INetCashProjectionMetric.ProjectedMonths;
        if (horizon is < 1 or > INetCashProjectionMetric.ProjectedMonths)
        {
            return Invalid("horizon", $"horizon must be between 1 and {INetCashProjectionMetric.ProjectedMonths}.");
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
        NetCollectionsPoint? actual = await LatestActualAsync(periods, id, end, filter, cancellationToken);
        NetCashProjectionView view = NetCashProjectionMath.Build(projection.Months, horizon, actual?.Value);
        NetCashProjectionResponse response = new(
            new MetricResponse(projection.Metric.Key, projection.Metric.Name, projection.Metric.Description, projection.Metric.Unit),
            providerId,
            projection.AfterPeriod.ToString(),
            ServiceLineName(filter),
            projection.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            view.Horizon.Value,
            view.Horizon.Low,
            view.Horizon.High,
            view.Steps.Select(ToMonth).ToArray())
        {
            Horizon = new NetCashProjectionHorizonResponse(
                view.Horizon.Value,
                view.Horizon.Low,
                view.Horizon.High,
                view.Steps.Count,
                view.HorizonComponents.FromExistingTrips,
                view.HorizonComponents.FromFutureTrips,
                view.HorizonComponents.ExistingTripsShare,
                view.HorizonComponents.FutureTripsShare),
            LatestActual = actual is null ? null : new NetCashProjectionActualResponse(actual.Period.ToString(), actual.Value),
        };
        return Results.Ok(response with { DataThrough = through });
    }

    private static NetCashProjectionMonthResponse ToMonth(NetCashProjectionStep step) => new(
        step.Month.Period.ToString(),
        Months[step.Month.Period.Month - 1],
        step.Month.Period.Year,
        step.Month.Value,
        step.Month.Low,
        step.Month.High,
        step.ChangeFromPrevious is { } change ? new AmountChangeResponse(change.Amount, change.Percent) : null,
        new NetCashProjectionRunningResponse(step.Cumulative.Value, step.Cumulative.Low, step.Cumulative.High),
        step.Components.FromExistingTrips,
        step.Components.FromFutureTrips,
        step.Components.ExistingTripsShare,
        step.Components.FutureTripsShare);

    // The newest month that has posted net cash among the six ending at the chosen period. None when the Provider or Service Line has no actual cash in that window.
    private static async Task<NetCollectionsPoint?> LatestActualAsync(INetCollectionsMetric actuals, int id, ApPeriod end, ServiceLineFilter filter, CancellationToken cancellationToken)
    {
        NetCollectionsSeries series = await actuals.GetSeriesAsync(id, end, ActualLookbackMonths, filter, cancellationToken);
        return series.Points.OrderByDescending(p => p.Period, Comparer<ApPeriod>.Create(ComparePeriods)).FirstOrDefault();
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
    string? ServiceLine,
    int? Horizon);

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
    /// <summary>The total of the returned months: <c>Value</c>, <c>Low</c>, <c>High</c> and how many <c>Months</c> that is.</summary>
    public NetCashProjectionHorizonResponse Horizon { get; init; } = new(0m, 0m, 0m, 0, null, null, null, null);

    /// <summary>The latest month with posted net cash that the first projected month is compared with. Null when there is none.</summary>
    public NetCashProjectionActualResponse? LatestActual { get; init; }

    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }
}

/// <summary>
/// One projected month: <c>Period</c> is <c>yyyyMM</c>, <c>Value</c> the projected cash, <c>Low</c> to <c>High</c> the likely range (a product rule, not a statistical interval).
/// <c>FromExistingTrips</c> and <c>FromFutureTrips</c> (USD, null when missing) are the warehouse's expected collections on trips that already existed and assumed run rate of trips that do not exist yet,
/// as read, with each one's share (0 to 1) of <c>Value</c> (null when <c>Value</c> is zero or the component is null). They are not assumed to sum to <c>Value</c>.
/// <c>ChangeFromPrevious</c> is null when there is no previous month to compare with. <c>Cumulative</c> adds the months up to and including this one.
/// </summary>
public sealed record NetCashProjectionMonthResponse(
    string Period,
    string Label,
    int Year,
    decimal Value,
    decimal Low,
    decimal High,
    AmountChangeResponse? ChangeFromPrevious,
    NetCashProjectionRunningResponse Cumulative,
    decimal? FromExistingTrips,
    decimal? FromFutureTrips,
    decimal? ExistingTripsShare,
    decimal? FutureTripsShare);

/// <summary>A change in USD (<c>Amount</c>) and in percent units (<c>Percent</c>, 12.5 is 12.5%, one decimal; null when the base is zero).</summary>
public sealed record AmountChangeResponse(decimal Amount, decimal? Percent);

/// <summary>A running sum of the projected value and its likely range.</summary>
public sealed record NetCashProjectionRunningResponse(decimal Value, decimal Low, decimal High);

/// <summary>The total over the returned months, and how many months that is.</summary>
public sealed record NetCashProjectionHorizonResponse(
    decimal Value,
    decimal Low,
    decimal High,
    int Months,
    decimal? FromExistingTrips,
    decimal? FromFutureTrips,
    decimal? ExistingTripsShare,
    decimal? FutureTripsShare);

/// <summary>An actual month of posted net cash: <c>Period</c> is <c>yyyyMM</c>, <c>Value</c> USD.</summary>
public sealed record NetCashProjectionActualResponse(string Period, decimal Value);
