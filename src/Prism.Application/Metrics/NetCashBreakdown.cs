namespace Prism.Application.Metrics;

/// <summary>
/// The figures for one slice of net cash in one accounting period. Transports are counted by posting month: the encounters that had net cash
/// posted in the period, however long ago they were flown or driven. <paramref name="PrimaryMix"/> and <paramref name="SecondaryMix"/> are the
/// counts behind the two shares on a card: scene and interfacility for air, emergency and non-emergency for ground.
/// </summary>
public sealed record BreakdownFigures(decimal Cash, int Transports, decimal LoadedMiles, int PrimaryMix, int SecondaryMix)
{
    public static BreakdownFigures None { get; } = new(0m, 0, 0m, 0, 0);

    public BreakdownFigures Plus(BreakdownFigures other) =>
        new(Cash + other.Cash, Transports + other.Transports, LoadedMiles + other.LoadedMiles, PrimaryMix + other.PrimaryMix, SecondaryMix + other.SecondaryMix);
}

/// <summary>An aircraft type (air) or a level of service (ground), with the chosen period and the one before it.</summary>
public sealed record NetCashBreakdownGroup(string Key, string Name, bool IsAir, BreakdownFigures Current, BreakdownFigures Prior);

/// <summary>One vehicle (tail number or unit) at one base or station, in its group. <paramref name="Base"/> is null when the warehouse has none.</summary>
public sealed record NetCashBreakdownRow(string GroupKey, string GroupName, bool IsAir, string? Base, string Vehicle, BreakdownFigures Current, BreakdownFigures Prior);

/// <summary>
/// Net cash for a period broken down by aircraft type or level of service, then by base and vehicle. <paramref name="PeriodOpen"/> is true while the
/// accounting period is still open, so cash is still posting into it and its figures are not final.
/// </summary>
public sealed record NetCashBreakdown(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    bool PeriodOpen,
    IReadOnlyList<NetCashBreakdownGroup> Groups,
    IReadOnlyList<NetCashBreakdownRow> Rows);

/// <summary>
/// Net cash split by what drove it, from the transaction ledger. Net cash means what the warehouse's net collections KPI means: credit transactions
/// that are payments or refunds, by the accounting period they posted in, without the Old AR import.
/// </summary>
public interface INetCashBreakdownMetric
{
    public static MetricDefinition Definition { get; } = new(
        "net_cash_breakdown",
        "Net cash by aircraft type or level of service",
        "Net cash collected in the accounting period (payments less refunds, by posting month, without the Old AR import), split by aircraft type or level of service and then by base and vehicle. Transports are the encounters with cash posted in the period.",
        "USD");

    Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, CancellationToken cancellationToken);
}
