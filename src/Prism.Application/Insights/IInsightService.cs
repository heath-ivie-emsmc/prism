using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Application.Insights;

/// <summary>An Insight with its words, and the Data Through date of the warehouse load it was computed from (null when the load has no date).</summary>
public sealed record InsightResult(Insight Insight, InsightNarrative Narrative, DateOnly? DataThrough);

/// <summary>
/// The "what changed" Insight for a Provider's group in a period. The Provider is always one the caller was already allowed to read in full.
/// Null means there is no cash in either month to explain.
/// </summary>
public interface IInsightService
{
    Task<InsightResult?> GetWhatChangedAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
