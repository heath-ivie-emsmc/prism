namespace Prism.Application.Metrics;

/// <summary>Small pure helpers the fleet, call type and pickup Metrics share, so a change and a share are worked out one way everywhere.</summary>
public static class MetricMath
{
    /// <summary>The change as percent units of the prior figure (12.5 is 12.5%), one decimal. Null when there was nothing before.</summary>
    public static decimal? ChangePercent(decimal current, decimal prior) =>
        prior == 0m ? null : Math.Round((current - prior) / prior * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>Part of a whole as a fraction from 0 to 1, four decimals. Null when the whole is zero.</summary>
    public static decimal? Share(decimal part, decimal whole) =>
        whole == 0m ? null : Math.Round(part / whole, 4, MidpointRounding.AwayFromZero);

    /// <summary>The move of a share in percentage points (one decimal), from the unrounded shares. Null when either month had no transports.</summary>
    public static decimal? ChangePoints(decimal part, decimal whole, decimal priorPart, decimal priorWhole) =>
        whole == 0m || priorWhole == 0m ? null : Math.Round(((part / whole) - (priorPart / priorWhole)) * 100m, 1, MidpointRounding.AwayFromZero);
}

/// <summary>The months a trend can show. Only these two are accepted, so the cache and the queries see a small set of shapes.</summary>
public static class TrendMonths
{
    public const int Default = 6;

    public static bool IsAllowed(int months) => months is 6 or 12;
}

/// <summary>
/// Transports and loaded miles by date of service and net cash by posting month for one slice, in the chosen month and the one before. Net cash per
/// transport is posting-month cash divided by date-of-service transports. It is a ratio of two bases, not the expected payment of a transport.
/// </summary>
public sealed record PeriodFigures(int Transports, int PriorTransports, decimal LoadedMiles, decimal PriorLoadedMiles, decimal NetCash, decimal PriorNetCash)
{
    public static PeriodFigures None { get; } = new(0, 0, 0m, 0m, 0m, 0m);

    public int ChangeTransports => Transports - PriorTransports;

    public decimal? NetCashPerTransport => Transports == 0 ? null : NetCash / Transports;

    public decimal? PriorNetCashPerTransport => PriorTransports == 0 ? null : PriorNetCash / PriorTransports;

    public PeriodFigures Plus(PeriodFigures other) =>
        new(Transports + other.Transports, PriorTransports + other.PriorTransports, LoadedMiles + other.LoadedMiles, PriorLoadedMiles + other.PriorLoadedMiles, NetCash + other.NetCash, PriorNetCash + other.PriorNetCash);
}

/// <summary>Transports of one payer group inside a slice, in the chosen month and the one before.</summary>
public sealed record PayerCount(string Key, string Name, int Transports, int PriorTransports);
