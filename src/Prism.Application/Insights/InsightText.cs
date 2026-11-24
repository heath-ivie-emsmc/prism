using System.Globalization;
using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>One card: its <paramref name="Kind"/> (a stable key for the analysis), a short title, a sentence or two, the label, and the dollar amount that goes beside it.</summary>
public sealed record InsightCard(string Kind, FindingLabel Label, FindingBasis Basis, string Title, string Body, decimal Impact);

/// <summary>The words for an Insight: a headline, the cards in order, and the line about what is left unexplained.</summary>
public sealed record InsightNarrative(string Headline, IReadOnlyList<InsightCard> Cards, string RemainderLine);

/// <summary>
/// Writes the text of an Insight from its Findings with fixed templates. Every number in the text comes from a Finding, so the words can never disagree
/// with the figures. The group name is shown as the caller gives it, so a Provider's own Label for it can be used.
/// </summary>
public static class InsightText
{
    public static InsightNarrative Build(Insight insight)
    {
        InsightCard[] cards = [.. insight.Drivers.Concat(insight.Expected).Select(f => Card(f, insight))];
        return new InsightNarrative(HeadlineText(insight), cards, RemainderText(insight.Remainder));
    }

    internal static string Money(decimal amount) => "$" + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    internal static string Percent(decimal ratio) => Math.Round(Math.Abs(ratio) * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    internal static string MonthName(ApPeriod period) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(period.Month);

    private static string Lower(string name) => name.ToLower(CultureInfo.InvariantCulture);

    /// <summary>The division the headline is limited to, as it is named in the warehouse (its case is kept).</summary>
    private static string InDivision(InsightHeadline h) => h.DivisionName is null ? string.Empty : $" in {h.DivisionName}";

    private static string HeadlineText(Insight insight)
    {
        InsightHeadline h = insight.Headline;
        string month = MonthName(h.PriorPeriod);
        if (h.Delta == 0m)
        {
            return $"{h.GroupName} cash{InDivision(h)} was unchanged from {month}.";
        }

        string verb = h.Delta < 0m ? "fell" : "rose";
        string size = h.DeltaRatio is { } ratio ? $"{Money(h.Delta)} ({Percent(ratio)})" : Money(h.Delta);
        string text = $"{h.GroupName} cash{InDivision(h)} {verb} {size} from {month}.";
        decimal temporary = insight.Drivers.Where(f => f.Label == FindingLabel.Temporary && Math.Sign(f.Impact) == Math.Sign(h.Delta)).Sum(f => f.Impact);
        return Math.Abs(temporary) * 2m > Math.Abs(h.Delta) ? text + " Most of it is timing, not a lasting change." : text;
    }

    private static InsightCard Card(Finding finding, Insight insight) => finding switch
    {
        AgedPaymentsFinding aged => AgedCard(aged, insight),
        VolumeFinding volume => VolumeCard(volume),
        PayerMixFinding mix => MixCard(mix, insight.Headline),
        AircraftMixFinding aircraft => SegmentCard("aircraft-mix", aircraft, insight.Headline),
        BaseMixFinding division => SegmentCard("base-mix", division, insight.Headline),
        _ => throw new ArgumentOutOfRangeException(nameof(finding), "Unknown Finding."),
    };

    private static InsightCard AgedCard(AgedPaymentsFinding aged, Insight insight)
    {
        InsightHeadline h = insight.Headline;
        bool inflatedLastMonth = aged.InflatedPeriod == h.PriorPeriod;
        string month = MonthName(aged.InflatedPeriod);
        decimal excess = inflatedLastMonth ? aged.ExcessPriorMonth : aged.ExcessThisMonth;
        string title = $"{month} had unusually high payments on older transports";
        string body = $"Payments on older transports in {month} ran {Money(excess)} above the usual level, across {aged.BatchTransports} transports.";
        if (insight.NormalizedDeltaRatio is { } normalized && h.DeltaRatio is { } actual)
        {
            string direction = normalized < 0m ? "fell" : "rose";
            body += $" Without that, {Lower(h.GroupName)} cash {direction} about {Percent(normalized)}, not {Percent(actual)}.";
        }

        return new InsightCard("aged-payments", aged.Label, aged.Basis, title, body, aged.Impact);
    }

    private static InsightCard VolumeCard(VolumeFinding volume)
    {
        string title = volume.Impact < 0m ? "Fewer transports had cash posted" : "More transports had cash posted";
        string body = $"Transports with cash posted went from {Count(volume.TransportsFrom)} to {Count(volume.TransportsTo)}, which moved cash by about {Money(volume.Impact)} at last month's cash per transport.";
        return new InsightCard("volume", volume.Label, volume.Basis, title, body, volume.Impact);
    }

    private static InsightCard MixCard(PayerMixFinding mix, InsightHeadline headline)
    {
        string group = Lower(headline.GroupName);
        string direction = mix.ShareTo > mix.ShareFrom ? "rose" : "fell";
        string title = mix.ShareTo > mix.ShareFrom ? $"More {group} transports were {mix.PayerName}" : $"Fewer {group} transports were {mix.PayerName}";
        string effect = mix.Impact < 0m ? "lower" : "higher";
        string body = $"{mix.PayerName}'s share of {group} transports {direction} from {Percent(mix.ShareFrom)} to {Percent(mix.ShareTo)}. "
            + $"At what transports have earned once paid, the shift in payer mix is expected to make cash about {Money(mix.Impact)} {effect} a month as those claims are paid. "
            + "This is an expected effect, not part of what moved this month's cash.";
        return new InsightCard("payer-mix", mix.Label, mix.Basis, title, body, mix.Impact);
    }

    /// <summary>
    /// The card for a shift of transports between types or divisions. The words name the segment and the dollar figures only: a Provider's own term for a Reporting Division
    /// is not known here, so the text does not use one.
    /// </summary>
    private static InsightCard SegmentCard(string kind, SegmentMixFinding mix, InsightHeadline headline)
    {
        string group = Lower(headline.GroupName);
        bool toward = mix.ShareTo > mix.ShareFrom;
        string title = (toward ? "Transports shifted toward " : "Transports shifted away from ") + mix.SegmentName;
        string rate = mix.SegmentRate >= mix.AverageRate ? "more" : "less";
        string effect = mix.Impact < 0m ? "lowered" : "raised";
        string body = $"{mix.SegmentName}'s share of {group} transports with cash posted went from {Percent(mix.ShareFrom)} to {Percent(mix.ShareTo)}. "
            + $"Last month a transport there brought in {Money(mix.SegmentRate)}, {rate} than the {Money(mix.AverageRate)} average, "
            + $"so the shift in mix {effect} posted cash by about {Money(mix.Impact)}.";
        if (mix.Basis == FindingBasis.AlternativeCut)
        {
            body += " This is another view of the same change, so it is not added to the drivers or the remainder.";
        }

        return new InsightCard(kind, mix.Label, mix.Basis, title, body, mix.Impact);
    }

    private static string Count(decimal transports) => Math.Round(transports, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string RemainderText(Remainder remainder) => remainder.Judgment == RemainderJudgment.WithinNormalRange
        ? $"The remaining {Money(remainder.Amount)} is normal month-to-month variation."
        : $"The remaining {Money(remainder.Amount)} is not explained by the drivers we can measure.";
}
