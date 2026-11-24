using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Tests.Insights;

/// <summary>The mix effect of transports moving between aircraft types or Reporting Divisions, and the two analyses built on it.</summary>
public sealed class SegmentMixTests
{
    private static readonly ApPeriod Oct = new(2026, 10);

    private static MixSegment Seg(string key, int transports, int priorTransports, decimal cash, decimal priorCash) => new(key, key, transports, priorTransports, cash, priorCash);

    [Fact]
    public void Moving_transports_toward_a_segment_that_earned_more_than_average_raises_cash_by_the_shift_times_the_difference()
    {
        // Last month: A 60 transports at 200, B 40 at 500, so the average is 320. This month 50 and 50: A lost 10 and B gained 10.
        SegmentMixResult result = SegmentMix.Compute([Seg("A", 50, 60, 9_000m, 12_000m), Seg("B", 50, 40, 25_000m, 20_000m)])!;

        Assert.Equal(3_000m, result.Impact);
        Assert.Equal(("B", 0.4m, 0.5m, 500m, 320m), (result.Biggest.Key, result.ShareFrom, result.ShareTo, result.SegmentRate, result.AverageRate));
    }

    [Fact]
    public void The_mix_effect_is_what_is_left_of_the_change_once_volume_and_rate_are_taken_out_for_any_segments()
    {
        var random = new Random(20261123);
        for (int round = 0; round < 50; round++)
        {
            MixSegment[] segments =
            [
                .. Enumerable.Range(0, random.Next(2, 6)).Select(i => Seg($"s{i}", random.Next(0, 80), random.Next(1, 80), random.Next(0, 90_000), random.Next(1, 90_000))),
            ];
            int now = segments.Sum(s => s.Transports);
            int before = segments.Sum(s => s.PriorTransports);
            if (SegmentMix.Compute(segments) is not { } result)
            {
                Assert.True(now == 0);
                continue;
            }

            // Price, volume and mix: this month's transports at last month's segment rates, less last month's cash scaled to this month's transports.
            decimal average = segments.Sum(s => s.PriorCash) / before;
            decimal atOldRates = segments.Sum(s => s.Transports * (s.PriorTransports == 0 ? average : s.PriorCash / s.PriorTransports));
            decimal expected = atOldRates - (decimal)now / before * segments.Sum(s => s.PriorCash);
            Assert.Equal((double)expected, (double)result.Impact, 4);
        }
    }

    [Fact]
    public void A_segment_with_no_transports_last_month_is_priced_at_the_average_and_adds_nothing()
    {
        SegmentMixResult? with = SegmentMix.Compute([Seg("A", 40, 50, 8_000m, 10_000m), Seg("B", 40, 50, 24_000m, 30_000m), Seg("NEW", 20, 0, 9_999m, 0m)]);
        SegmentMixResult? without = SegmentMix.Compute([Seg("A", 40, 50, 8_000m, 10_000m), Seg("B", 40, 50, 24_000m, 30_000m)]);

        Assert.NotNull(with);
        Assert.NotNull(without);
        // Nobody moved among the old segments relative to each other (they hold their share of the old 100 transports), so the new one's 20 transports are all the shift.
        Assert.Equal(0m, Math.Round(with.Impact, 6));
        Assert.Equal(0m, without.Impact);
    }

    [Fact]
    public void Nothing_to_compare_means_no_result()
    {
        Assert.Null(SegmentMix.Compute([]));
        Assert.Null(SegmentMix.Compute([Seg("A", 10, 10, 100m, 100m)]));
        Assert.Null(SegmentMix.Compute([Seg("A", 0, 10, 0m, 100m), Seg("B", 0, 10, 0m, 100m)]));
        Assert.Null(SegmentMix.Compute([Seg("A", 10, 0, 100m, 0m), Seg("B", 10, 0, 100m, 0m)]));
    }

    private static NetCashBreakdownRow Row(string group, string? division, int transports, int priorTransports, decimal cash, decimal priorCash) =>
        new(group, group == "rotor-wing" ? "Rotor wing" : "Fixed wing", true, division, "N1", new BreakdownFigures(cash, transports, 0m, 0, 0), new BreakdownFigures(priorCash, priorTransports, 0m, 0, 0));

    // Rotor 60 -> 50 transports at 200 each, fixed 40 -> 50 at 500 each. Divisions: east carries the rotor and west the fixed, so both cuts tell the same story.
    private static readonly NetCashBreakdownRow[] Rows =
    [
        Row("rotor-wing", "East", 50, 60, 9_000m, 12_000m),
        Row("fixed-wing", "West", 50, 40, 25_000m, 20_000m),
    ];

    private static InsightInput Input(string? group = null, params Finding[] earlier) =>
        new(7, Oct, ServiceLineFilter.Air, group, [], [.. earlier.Select(f => Task.FromResult<Finding?>(f))]);

    [Fact]
    public async Task The_aircraft_mix_is_a_driver_on_posted_cash_named_for_the_type_that_moved_most_and_reads_the_whole_provider()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);

        Finding? finding = await new AircraftMixAnalysis(breakdown).RunAsync(Input(), TestContext.Current.CancellationToken);

        AircraftMixFinding mix = Assert.IsType<AircraftMixFinding>(finding);
        Assert.Equal(("fixed-wing", "Fixed wing", 0.4m, 0.5m, 3_000m), (mix.SegmentKey, mix.SegmentName, mix.ShareFrom, mix.ShareTo, mix.Impact));
        Assert.Equal((FindingBasis.PostedCash, FindingLabel.Info), (mix.Basis, mix.Label));
        // Never one Reporting Division, whatever the dashboard shows: the Insight explains the Provider's own total.
        Assert.Equal([(7, Oct, ServiceLineFilter.Air, (ReportingDivision?)null)], breakdown.Calls);
    }

    [Fact]
    public async Task A_mix_that_lowered_cash_is_watch()
    {
        var breakdown = new InsightFakes.BreakdownFake([Row("rotor-wing", "East", 70, 60, 14_000m, 12_000m), Row("fixed-wing", "West", 30, 40, 15_000m, 20_000m)]);

        AircraftMixFinding mix = Assert.IsType<AircraftMixFinding>(await new AircraftMixAnalysis(breakdown).RunAsync(Input(), TestContext.Current.CancellationToken));

        Assert.True(mix.Impact < 0m);
        Assert.Equal(FindingLabel.Watch, mix.Label);
    }

    [Fact]
    public async Task One_chosen_group_a_single_type_and_a_tiny_move_say_nothing_for_aircraft()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);
        var single = new InsightFakes.BreakdownFake([Rows[0]]);
        var tiny = new InsightFakes.BreakdownFake([Row("rotor-wing", "East", 600, 600, 9_000m, 12_000m), Row("fixed-wing", "West", 400, 404, 25_000m, 20_000m)]);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Null(await new AircraftMixAnalysis(breakdown).RunAsync(Input("fixed-wing"), ct));
        Assert.Empty(breakdown.Calls);
        Assert.Null(await new AircraftMixAnalysis(single).RunAsync(Input(), ct));
        Assert.Null(await new AircraftMixAnalysis(tiny).RunAsync(Input(), ct));
    }

    [Fact]
    public async Task The_base_mix_is_a_driver_when_no_aircraft_mix_explains_the_change_and_otherwise_sits_beside_the_drivers()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);
        var aircraft = new AircraftMixFinding(FindingLabel.Info, 3_000m, "fixed-wing", "Fixed wing", 0.4m, 0.5m, 500m, 320m);
        CancellationToken ct = TestContext.Current.CancellationToken;

        BaseMixFinding alone = Assert.IsType<BaseMixFinding>(await new BaseMixAnalysis(breakdown).RunAsync(Input(), ct));
        BaseMixFinding beside = Assert.IsType<BaseMixFinding>(await new BaseMixAnalysis(breakdown).RunAsync(Input(null, aircraft), ct));

        Assert.Equal(FindingBasis.PostedCash, alone.Basis);
        Assert.Equal(FindingBasis.AlternativeCut, beside.Basis);
        Assert.Equal(("West", 3_000m), (beside.SegmentName, beside.Impact));
    }

    [Fact]
    public async Task With_a_division_chosen_the_base_mix_is_left_out_and_the_aircraft_mix_is_worked_out_inside_that_division()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);
        var rotor = new ReportingDivision("rotor", "Mercy Flights - Rotor");
        InsightInput input = Input() with { Division = rotor };
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Null(await new BaseMixAnalysis(breakdown).RunAsync(input, ct));
        Assert.Empty(breakdown.Calls);
        Assert.IsType<AircraftMixFinding>(await new AircraftMixAnalysis(breakdown).RunAsync(input, ct));
        Assert.Equal([(7, Oct, ServiceLineFilter.Air, (ReportingDivision?)rotor)], breakdown.Calls);
    }

    [Fact]
    public async Task In_the_engine_a_division_limits_the_headline_and_the_drivers_and_they_still_add_up_to_the_change_within_it()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);
        var cash = new InsightFakes.NetCashByGroupFake(
        [
            new NetCashByGroupPoint("rotor-wing", "Rotor wing", true, Oct.AddMonths(-1), 12_000m, 60),
            new NetCashByGroupPoint("fixed-wing", "Fixed wing", true, Oct.AddMonths(-1), 20_000m, 40),
            new NetCashByGroupPoint("rotor-wing", "Rotor wing", true, Oct, 9_000m, 50),
            new NetCashByGroupPoint("fixed-wing", "Fixed wing", true, Oct, 25_000m, 50),
        ]);
        var engine = new InsightsEngine(cash, [new AircraftMixAnalysis(breakdown), new BaseMixAnalysis(breakdown)]);
        var rotor = new ReportingDivision("rotor", "Mercy Flights - Rotor");

        Insight? insight = await engine.ExplainAsync(7, Oct, ServiceLineFilter.Air, null, rotor, TestContext.Current.CancellationToken);

        Assert.NotNull(insight);
        Assert.Equal(rotor.Name, insight.Headline.DivisionName);
        Assert.Equal([rotor], cash.Divisions.Distinct());
        Assert.DoesNotContain(insight.Drivers.Concat(insight.Expected), f => f is BaseMixFinding);
        Assert.Equal(insight.Headline.Delta, insight.Drivers.Sum(f => f.Impact) + insight.Remainder.Amount);
        Assert.Equal([rotor], breakdown.Calls.Select(c => c.Division).Distinct());
        Assert.StartsWith("Air cash in Mercy Flights - Rotor rose", InsightText.Build(insight).Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_base_mix_with_one_group_chosen_looks_only_at_that_groups_divisions_and_a_missing_division_is_not_recorded()
    {
        var breakdown = new InsightFakes.BreakdownFake(
        [
            Row("rotor-wing", "East", 30, 40, 6_000m, 8_000m),
            Row("rotor-wing", null, 40, 30, 12_000m, 3_000m),
            Row("fixed-wing", "West", 900, 100, 1m, 1m),
        ]);

        BaseMixFinding mix = Assert.IsType<BaseMixFinding>(await new BaseMixAnalysis(breakdown).RunAsync(Input("rotor-wing"), TestContext.Current.CancellationToken));

        Assert.Equal(("not-recorded", "Not recorded"), (mix.SegmentKey, mix.SegmentName));
    }

    [Fact]
    public async Task In_the_engine_the_aircraft_mix_is_subtracted_as_a_driver_the_base_mix_is_not_and_the_change_still_adds_up()
    {
        var breakdown = new InsightFakes.BreakdownFake(Rows);
        // Cash 32,000 last month and 34,000 this month: the change is +2,000, the mix is worth +3,000, so something else took 1,000 away.
        var cash = new InsightFakes.NetCashByGroupFake(
        [
            new NetCashByGroupPoint("rotor-wing", "Rotor wing", true, Oct.AddMonths(-1), 12_000m, 60),
            new NetCashByGroupPoint("fixed-wing", "Fixed wing", true, Oct.AddMonths(-1), 20_000m, 40),
            new NetCashByGroupPoint("rotor-wing", "Rotor wing", true, Oct, 9_000m, 50),
            new NetCashByGroupPoint("fixed-wing", "Fixed wing", true, Oct, 25_000m, 50),
        ]);
        var engine = new InsightsEngine(cash, [new AircraftMixAnalysis(breakdown), new BaseMixAnalysis(breakdown)]);

        Insight? insight = await engine.ExplainAsync(7, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.NotNull(insight);
        Assert.Equal(2_000m, insight.Headline.Delta);
        Assert.Equal(["fixed-wing"], insight.Drivers.OfType<AircraftMixFinding>().Select(f => f.SegmentKey));
        BaseMixFinding beside = Assert.IsType<BaseMixFinding>(Assert.Single(insight.Expected));
        Assert.Equal(FindingBasis.AlternativeCut, beside.Basis);
        Assert.Equal(3_000m, insight.Drivers.Sum(f => f.Impact));
        Assert.Equal(-1_000m, insight.Remainder.Amount);
        Assert.Equal(insight.Headline.Delta, insight.Drivers.Sum(f => f.Impact) + insight.Remainder.Amount);

        InsightNarrative text = InsightText.Build(insight);
        Assert.Equal(["aircraft-mix", "base-mix"], text.Cards.Select(c => c.Kind));
        Assert.Contains("not added to the drivers", text.Cards[1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("not added to the drivers", text.Cards[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cards_name_the_segment_and_say_why_in_figures_taken_from_the_finding_and_never_use_a_term_a_provider_can_rename()
    {
        var up = new AircraftMixFinding(FindingLabel.Info, 3_000m, "fixed-wing", "Fixed wing", 0.4m, 0.5m, 500m, 320m);
        var down = new BaseMixFinding(FindingLabel.Watch, FindingBasis.PostedCash, -1_250.5m, "East", "East", 0.6m, 0.5m, 200m, 320m);
        var headline = new InsightHeadline(Oct, Oct.AddMonths(-1), null, "Air", 34_000m, 32_000m, 2_000m, 0.0625m);

        InsightNarrative text = InsightText.Build(new Insight(headline, [up, down], [], new Remainder(0m, RemainderJudgment.NotEnoughHistory), null));

        Assert.Equal("Transports shifted toward Fixed wing", text.Cards[0].Title);
        Assert.Equal(
            "Fixed wing's share of air transports with cash posted went from 40% to 50%. Last month a transport there brought in $500.00, more than the $320.00 average, so the shift in mix raised posted cash by about $3,000.00.",
            text.Cards[0].Body);
        Assert.Equal("Transports shifted away from East", text.Cards[1].Title);
        Assert.Contains("less than the $320.00 average, so the shift in mix lowered posted cash by about $1,250.50.", text.Cards[1].Body, StringComparison.Ordinal);
        Assert.All(text.Cards, c => Assert.DoesNotContain("base", c.Title + c.Body, StringComparison.OrdinalIgnoreCase));
        Assert.All(text.Cards, c => Assert.DoesNotContain("Reporting Division", c.Title + c.Body, StringComparison.OrdinalIgnoreCase));
    }
}
