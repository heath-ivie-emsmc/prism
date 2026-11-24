namespace Prism.Application.Insights;

/// <summary>One segment (an aircraft type or a Reporting Division) with the transports that had cash posted and the cash, this month and last.</summary>
public sealed record MixSegment(string Key, string Name, int Transports, int PriorTransports, decimal Cash, decimal PriorCash);

/// <summary>The mix effect of a set of segments, and the segment that moved it most.</summary>
public sealed record SegmentMixResult(decimal Impact, MixSegment Biggest, decimal ShareFrom, decimal ShareTo, decimal SegmentRate, decimal AverageRate);

/// <summary>
/// The mix part of the usual price, volume and mix split of a change in cash. With <c>Tn</c> and <c>Tp</c> the transports this month and last, <c>r</c> a segment's cash per
/// transport last month and <c>R</c> the average of all segments last month, the mix effect is the sum over segments of (<c>Tn</c> in the segment minus its last-month share
/// of <c>Tn</c>) times <c>r</c>. Because those differences add up to zero it equals the sum of (shift) times (<c>r</c> minus <c>R</c>): moving transports toward a segment that
/// earned more than average raises it. A segment with no transports last month has no rate, so it is priced at <c>R</c> and adds nothing, and its cash stays in the remainder.
/// </summary>
public static class SegmentMix
{
    public static SegmentMixResult? Compute(IReadOnlyList<MixSegment> segments)
    {
        int now = segments.Sum(s => s.Transports);
        int before = segments.Sum(s => s.PriorTransports);
        if (now == 0 || before == 0 || segments.Count(s => s.Transports > 0 || s.PriorTransports > 0) < 2)
        {
            return null;
        }

        decimal average = segments.Sum(s => s.PriorCash) / before;
        MixSegment? biggest = null;
        decimal biggestEffect = 0m;
        decimal total = 0m;
        foreach (MixSegment s in segments)
        {
            decimal shift = s.Transports - ((decimal)now * s.PriorTransports / before);
            decimal rate = s.PriorTransports == 0 ? average : s.PriorCash / s.PriorTransports;
            decimal effect = shift * (rate - average);
            total += effect;
            if (biggest is null || Math.Abs(effect) > Math.Abs(biggestEffect))
            {
                (biggest, biggestEffect) = (s, effect);
            }
        }

        if (biggest is null)
        {
            return null;
        }

        decimal segmentRate = biggest.PriorTransports == 0 ? average : biggest.PriorCash / biggest.PriorTransports;
        return new SegmentMixResult(total, biggest, (decimal)biggest.PriorTransports / before, (decimal)biggest.Transports / now, segmentRate, average);
    }
}
