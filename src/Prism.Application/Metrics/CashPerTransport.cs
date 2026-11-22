namespace Prism.Application.Metrics;

/// <summary>
/// One payer group's transports and the net cash posted for them. <see cref="CashPerTransport"/> is null when the group had no transports,
/// so a missing average is never shown as zero.
/// </summary>
public sealed record PayerCashPerTransport(string Key, string Name, int Transports, decimal Cash)
{
    public decimal? CashPerTransport => Transports == 0 ? null : Cash / Transports;
}

/// <summary>
/// What a transport earns by payer group, for transports that are old enough to have been paid. The cohorts are the twelve date-of-service months that
/// ended <see cref="ICashPerTransportMetric.MaturityMonths"/> or more months before <paramref name="Period"/>, from <paramref name="CohortFrom"/> to
/// <paramref name="CohortThrough"/>. <paramref name="GroupKey"/> narrows it to one aircraft type or level of service, and is null for the whole Service Line.
/// <paramref name="Payers"/> always lists every payer group, in a fixed order, with zeros.
/// </summary>
public sealed record CashPerTransport(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod CohortFrom,
    ApPeriod CohortThrough,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<PayerCashPerTransport> Payers);

/// <summary>
/// Matured net cash per transport by primary payer group. Net cash is payments less refunds, without the Old AR import, posted up to and including the
/// chosen period, for every transport in the cohorts, including transports nobody has paid yet. Transports with no payer are in the Not recorded group.
/// Only transports at least six months old count, so a recent month's unpaid claims do not look like a low rate. Aggregates only.
/// </summary>
public interface ICashPerTransportMetric
{
    /// <summary>A transport counts once it is this many months old or more.</summary>
    public const int MaturityMonths = 6;

    /// <summary>How many date-of-service months make up the cohorts.</summary>
    public const int CohortMonths = 12;

    public static MetricDefinition Definition { get; } = new(
        "cash_per_transport_by_payer",
        "Matured net cash per transport by payer",
        "Net cash posted up to the chosen period divided by transports, by the primary payer's group, for the twelve months of service that ended at least six months before the chosen period.",
        "USD per transport");

    Task<CashPerTransport> GetCashPerTransportAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken);
}
