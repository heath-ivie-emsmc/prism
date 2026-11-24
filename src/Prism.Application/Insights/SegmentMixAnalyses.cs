using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>
/// Shared by the aircraft and base mix analyses: reads the net cash breakdown for the Service Line (the whole Provider, never one Reporting Division), builds the segments, works
/// out the mix effect and applies the thresholds. It reads a Metric only, never the warehouse.
/// </summary>
internal static class SegmentMixReader
{
    /// <summary>The segments the breakdown shows, for the group being explained when there is one.</summary>
    internal static async Task<IReadOnlyList<MixSegment>> ReadAsync(INetCashBreakdownMetric breakdown, InsightInput input, bool byBase, CancellationToken cancellationToken)
    {
        NetCashBreakdown result = await breakdown.GetBreakdownAsync(input.ProviderId, input.Period, input.ServiceLine, null, cancellationToken);
        IEnumerable<NetCashBreakdownRow> rows = input.GroupKey is null ? result.Rows : result.Rows.Where(r => r.GroupKey == input.GroupKey);
        return byBase
            ? [.. rows.GroupBy(r => r.Base ?? string.Empty).Select(g => Segment(g.Key.Length == 0 ? "not-recorded" : g.Key, g.Key.Length == 0 ? "Not recorded" : g.Key, g.Select(r => r.Current), g.Select(r => r.Prior)))]
            : [.. result.Groups.Where(g => input.GroupKey is null || g.Key == input.GroupKey).Select(g => Segment(g.Key, g.Name, [g.Current], [g.Prior]))];
    }

    private static MixSegment Segment(string key, string name, IEnumerable<BreakdownFigures> current, IEnumerable<BreakdownFigures> prior) =>
        new(key, name, current.Sum(f => f.Transports), prior.Sum(f => f.Transports), current.Sum(f => f.Cash), prior.Sum(f => f.Cash));

    /// <summary>The result when the biggest segment's share moved far enough to say something, otherwise null.</summary>
    internal static SegmentMixResult? Material(IReadOnlyList<MixSegment> segments) =>
        SegmentMix.Compute(segments) is { } result && Math.Abs(result.ShareTo - result.ShareFrom) >= InsightThresholds.MinShareMove ? result : null;

    internal static FindingLabel LabelOf(decimal impact) => impact < 0m ? FindingLabel.Watch : FindingLabel.Info;
}

/// <summary>
/// How much of the change is transports moving between aircraft types (air) or levels of service (ground) that earn different amounts. It is a Driver, because it is
/// a part of the posted change: the volume Driver prices every transport at the average, and this is what is left once the types are priced separately. It is not shown when
/// one group is chosen (there is nothing to move between). It is Watch when it lowered cash and Info when it raised it: whether it held for more than a month is not checked.
/// </summary>
public sealed class AircraftMixAnalysis(INetCashBreakdownMetric breakdown) : IInsightAnalysis
{
    public int Order => 40;

    public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
    {
        if (input.GroupKey is not null)
        {
            return null;
        }

        IReadOnlyList<MixSegment> segments = await SegmentMixReader.ReadAsync(breakdown, input, false, cancellationToken);
        return SegmentMixReader.Material(segments) is { } r
            ? new AircraftMixFinding(SegmentMixReader.LabelOf(r.Impact), r.Impact, r.Biggest.Key, r.Biggest.Name, r.ShareFrom, r.ShareTo, r.SegmentRate, r.AverageRate)
            : null;
    }
}

/// <summary>
/// The same mix effect between Reporting Divisions. A change can be cut by type or by division but not both, so when an aircraft-mix Finding exists this one is
/// <see cref="FindingBasis.AlternativeCut"/>: shown beside the Drivers, never added to them. When none exists (one group chosen, or no aircraft mix to speak of) it is a Driver.
/// </summary>
public sealed class BaseMixAnalysis(INetCashBreakdownMetric breakdown) : IInsightAnalysis
{
    public int Order => 41;

    public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
    {
        // The aircraft analysis is started before this one and normally runs on the same cached read, so waiting for it costs little.
        Task<IReadOnlyList<MixSegment>> read = SegmentMixReader.ReadAsync(breakdown, input, true, cancellationToken);
        bool aircraftExplains = (await input.FindingsBeforeAsync()).OfType<AircraftMixFinding>().Any();
        SegmentMixResult? result = SegmentMixReader.Material(await read);
        if (result is null)
        {
            return null;
        }

        FindingBasis basis = aircraftExplains ? FindingBasis.AlternativeCut : FindingBasis.PostedCash;
        return new BaseMixFinding(SegmentMixReader.LabelOf(result.Impact), basis, result.Impact, result.Biggest.Key, result.Biggest.Name, result.ShareFrom, result.ShareTo, result.SegmentRate, result.AverageRate);
    }
}
