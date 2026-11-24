namespace Prism.Application.Metrics;

/// <summary>Which Service Lines a figure covers. Matches the dashboard toggle.</summary>
public enum ServiceLineFilter
{
    Both,
    Air,
    Ground,
}

/// <summary>
/// One projected month. <paramref name="Value"/> is the total projected cash. <paramref name="FromExistingTrips"/> is the expected collections on trips that already existed
/// (<c>forecasted_ar_cash</c>) and <paramref name="FromFutureTrips"/> the assumed run rate of trips that do not exist yet (<c>forecasted_budget_cash</c>); each is null when the warehouse has none.
/// They are reported as read: nothing here assumes they add up to the total. <paramref name="Low"/> and <paramref name="High"/> are the
/// smaller and larger of the AR-based and budget-based forecasts (and the total), the "likely range".
/// </summary>
public sealed record NetCashProjectionMonth(ApPeriod Period, decimal Value, decimal Low, decimal High, decimal? FromExistingTrips = null, decimal? FromFutureTrips = null);

/// <summary>The months after a chosen period that the warehouse has a cash forecast for, with their sums.</summary>
public sealed record NetCashProjection(
    MetricDefinition Metric,
    ApPeriod AfterPeriod,
    ServiceLineFilter ServiceLine,
    DateOnly? SnapshotDate,
    IReadOnlyList<NetCashProjectionMonth> Months)
{
    public decimal Total => Months.Sum(m => m.Value);

    public decimal Low => Months.Sum(m => m.Low);

    public decimal High => Months.Sum(m => m.High);
}

/// <summary>A change in USD and in percent of the figure it is measured from. <paramref name="Percent"/> is in percent units (12.5 is 12.5%), rounded to one decimal, and null when that figure is zero.</summary>
public sealed record AmountChange(decimal Amount, decimal? Percent);

/// <summary>A running sum of value, low and high.</summary>
public sealed record RunningTotal(decimal Value, decimal Low, decimal High);

/// <summary>A sum of the two components over some months, each null when no month had it, with each one's share (0 to 1, four decimals) of the value; null when the value is zero or the component is null.</summary>
public sealed record ProjectionComponents(decimal? FromExistingTrips, decimal? FromFutureTrips, decimal? ExistingTripsShare, decimal? FutureTripsShare)
{
    public static ProjectionComponents Of(decimal? existing, decimal? future, decimal value) =>
        new(existing, future, Share(existing, value), Share(future, value));

    private static decimal? Share(decimal? part, decimal value) =>
        part is { } p && value != 0m ? Math.Round(p / value, 4, MidpointRounding.AwayFromZero) : null;
}

/// <summary>A projected month with its change from the month before and the running total from the first returned month.</summary>
public sealed record NetCashProjectionStep(NetCashProjectionMonth Month, AmountChange? ChangeFromPrevious, RunningTotal Cumulative)
{
    public ProjectionComponents Components => ProjectionComponents.Of(Month.FromExistingTrips, Month.FromFutureTrips, Month.Value);
}

/// <summary>The projected months within a horizon, each with its change and running total, and the horizon's total.</summary>
public sealed record NetCashProjectionView(IReadOnlyList<NetCashProjectionStep> Steps, RunningTotal Horizon, ProjectionComponents HorizonComponents);

/// <summary>Derives the change from the previous month and the running totals. Pure arithmetic over figures already read.</summary>
public static class NetCashProjectionMath
{
    /// <summary>
    /// The first <paramref name="horizon"/> months. The first month's change is measured from <paramref name="latestActual"/> (null when there is none, so the change is null);
    /// each later month is measured from the month before it.
    /// </summary>
    public static NetCashProjectionView Build(IReadOnlyList<NetCashProjectionMonth> months, int horizon, decimal? latestActual)
    {
        var steps = new List<NetCashProjectionStep>();
        decimal value = 0m;
        decimal low = 0m;
        decimal high = 0m;
        decimal? previous = latestActual;
        decimal? existing = null;
        decimal? future = null;
        foreach (NetCashProjectionMonth month in months.Take(Math.Max(horizon, 0)))
        {
            value += month.Value;
            low += month.Low;
            high += month.High;
            existing = Add(existing, month.FromExistingTrips);
            future = Add(future, month.FromFutureTrips);
            steps.Add(new NetCashProjectionStep(month, previous is { } from ? Change(month.Value, from) : null, new RunningTotal(value, low, high)));
            previous = month.Value;
        }

        return new NetCashProjectionView(steps, new RunningTotal(value, low, high), ProjectionComponents.Of(existing, future, value));
    }

    private static decimal? Add(decimal? sum, decimal? part) => part is null ? sum : (sum ?? 0m) + part;

    private static AmountChange Change(decimal current, decimal from) =>
        new(current - from, from == 0m ? null : Math.Round((current - from) / Math.Abs(from) * 100m, 1, MidpointRounding.AwayFromZero));
}

/// <summary>The cash forecast for the next few months, from <c>reporting.forecast_cash_per_trip</c>.</summary>
public interface INetCashProjectionMetric
{
    public const int ProjectedMonths = 3;

    public static MetricDefinition Definition { get; } = new(
        "net_cash_projection",
        "Projected net cash",
        "Cash the data warehouse forecasts for up to three months after the chosen period, from the latest forecast load, with each month's change from the month before and a running total. The range runs from the lower to the higher of the total, AR-based and budget-based forecasts. It is not a statistical interval.",
        "USD");

    /// <summary>The next <see cref="ProjectedMonths"/> months after <paramref name="afterPeriod"/> that have a forecast. Fewer, or none, when the forecast is shorter.</summary>
    Task<NetCashProjection> GetProjectionAsync(int providerId, ApPeriod afterPeriod, ServiceLineFilter serviceLine, CancellationToken cancellationToken);
}
