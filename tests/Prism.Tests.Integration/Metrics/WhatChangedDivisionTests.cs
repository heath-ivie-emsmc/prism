using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// The "what changed" Insight run end to end (the real Metrics and every analysis) against a real PostgreSQL holding the trip table and the ledger, with a Reporting Division
/// chosen. The point of every test is that the headline, the Drivers and the remainder still add up inside the division.
/// </summary>
public sealed class WhatChangedDivisionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    // Rotor division: enc 1 and 7 (rotor wing) and 5 (fixed wing) in October, enc 2 (rotor wing) and 6 (fixed wing) in September. Fixed division: enc 3 and 4. Enc 8 has no division.
    private static readonly SeedTrip[] Trips =
    [
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 100, Rotor),
        new(2, "Rotor Wing", null, "MEDICARE", "Medicare", "2026-09-04", 50, Rotor),
        new(3, "Fixed Wing", null, "MEDICAID", "Medicaid", "2026-10-05", 30, FixedLegacy),
        new(4, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-06", 200, Fixed),
        new(5, "Fixed Wing", null, "AETNA", "Insurance", "2026-10-07", 20, Rotor),
        new(6, "Fixed Wing", null, "MEDICARE", "Medicare", "2026-09-08", 20, Rotor),
        new(7, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-09", 40, Rotor),
        new(8, "Rotor Wing", null, "ODD", "Something Else", "2026-10-09", 5, "<none>"),
        new(11, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-04", 5000, "Other Provider - Base", Provider: 777),
    ];

    // Posting periods: 1 is 202610, 2 is 202609. Rotor: October 1000 + 3000 + 700 = 4700, September 600 + 2000 = 2600. Fixed: October 500, September 400 + 250.
    private static readonly SeedCash[] Cash =
    [
        new(1, 1, 1, 100000),
        new(2, 2, 2, 60000),
        new(3, 3, 1, 50000),
        new(4, 4, 2, 40000),
        new(5, 5, 1, 300000),
        new(6, 6, 2, 200000),
        new(7, 7, 1, 70000),
        new(8, 8, 1, 1000),
        new(9, 3, 2, 25000),
        new(10, 11, 1, 500000),
    ];

    private async Task<InsightsEngine> NewAsync()
    {
        IWarehouse warehouse = await CreateAsync(postgres, Trips, Cash, TestContext.Current.CancellationToken);
        var providers = new FakeProviderResolver(null);
        var payerMix = new PayerMixMetric(warehouse, providers);
        var cashPerTransport = new CashPerTransportMetric(warehouse, providers);
        var breakdown = new NetCashBreakdownMetric(warehouse, providers);
        return new InsightsEngine(
            new NetCashByGroupMetric(warehouse, providers),
            [new AgedPaymentsAnalysis(new PaymentAgeMetric(warehouse, providers)), new VolumeAnalysis(), new PayerMixAnalysis(payerMix, cashPerTransport), new AircraftMixAnalysis(breakdown), new BaseMixAnalysis(breakdown)]);
    }

    private static Task<Insight?> ExplainAsync(InsightsEngine engine, ReportingDivision? division) =>
        engine.ExplainAsync(2026, Oct, ServiceLineFilter.Air, null, division, TestContext.Current.CancellationToken);

    private static void AssertAddsUp(Insight insight) =>
        Assert.Equal(insight.Headline.Delta, insight.Drivers.Sum(f => f.Impact) + insight.Remainder.Amount);

    [Fact]
    public async Task A_division_narrows_the_figures_and_every_division_still_adds_up_to_its_own_change()
    {
        InsightsEngine engine = await NewAsync();

        Insight all = (await ExplainAsync(engine, null))!;
        Insight rotor = (await ExplainAsync(engine, RotorDivision))!;
        Insight fixedWing = (await ExplainAsync(engine, FixedDivision))!;

        // Air only: the no-division $10 and every division's cash are counted for all divisions, and the divisions of the Provider add up to it less that $10.
        Assert.Equal((4700m, 2600m, 2100m), (rotor.Headline.CashNow, rotor.Headline.CashPrior, rotor.Headline.Delta));
        Assert.Equal((500m, 650m), (fixedWing.Headline.CashNow, fixedWing.Headline.CashPrior));
        Assert.Equal(all.Headline.CashNow - 10m, rotor.Headline.CashNow + fixedWing.Headline.CashNow);
        Assert.Equal(all.Headline.CashPrior, rotor.Headline.CashPrior + fixedWing.Headline.CashPrior);
        Assert.Equal(RotorDivision.Name, rotor.Headline.DivisionName);
        Assert.Null(all.Headline.DivisionName);
        AssertAddsUp(all);
        AssertAddsUp(rotor);
        AssertAddsUp(fixedWing);
        // Three transports against two in the division, priced at last month's cash per transport there.
        Assert.Contains(rotor.Drivers, f => f is VolumeFinding);
    }

    [Fact]
    public async Task A_division_never_gets_the_base_mix_which_is_a_cut_across_divisions()
    {
        InsightsEngine engine = await NewAsync();

        Insight all = (await ExplainAsync(engine, null))!;
        Insight rotor = (await ExplainAsync(engine, RotorDivision))!;

        Assert.Contains(all.Drivers.Concat(all.Expected), f => f is BaseMixFinding);
        Assert.DoesNotContain(rotor.Drivers.Concat(rotor.Expected), f => f is BaseMixFinding);
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("x' OR '1'='1")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_so_there_is_nothing_to_explain(string name)
    {
        InsightsEngine engine = await NewAsync();

        Assert.Null(await ExplainAsync(engine, new ReportingDivision("k", name)));
    }
}
