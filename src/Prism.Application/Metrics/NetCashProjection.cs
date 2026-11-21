namespace Prism.Application.Metrics;

/// <summary>Which Service Lines a figure covers. Matches the dashboard toggle.</summary>
public enum ServiceLineFilter
{
    Both,
    Air,
    Ground,
}

/// <summary>
/// One projected month. <paramref name="Value"/> is the total projected cash. <paramref name="Low"/> and <paramref name="High"/> are the
/// smaller and larger of the AR-based and budget-based forecasts (and the total), the "likely range".
/// </summary>
public sealed record NetCashProjectionMonth(ApPeriod Period, decimal Value, decimal Low, decimal High);

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

/// <summary>The cash forecast for the next few months, from <c>reporting.forecast_cash_per_trip</c>.</summary>
public interface INetCashProjectionMetric
{
    public const int ProjectedMonths = 3;

    public static MetricDefinition Definition { get; } = new(
        "net_cash_projection",
        "Projected net cash",
        "Cash the data warehouse forecasts for the months after the chosen period, from the latest forecast load. The range runs from the lower to the higher of the AR-based and budget-based forecasts.",
        "USD");

    /// <summary>The next <see cref="ProjectedMonths"/> months after <paramref name="afterPeriod"/> that have a forecast. Fewer, or none, when the forecast is shorter.</summary>
    Task<NetCashProjection> GetProjectionAsync(int providerId, ApPeriod afterPeriod, ServiceLineFilter serviceLine, CancellationToken cancellationToken);
}
