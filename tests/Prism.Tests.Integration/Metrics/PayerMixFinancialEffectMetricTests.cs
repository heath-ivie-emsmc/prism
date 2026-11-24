using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Tests.Integration.Persistence;
using static Prism.Tests.Integration.Metrics.FleetSeed;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The payer mix effect over the real payer mix and cash per transport queries, against a PostgreSQL holding tables shaped like the trip table and the ledger.</summary>
public sealed class PayerMixFinancialEffectMetricTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ApPeriod Oct = new(2026, 10);

    // Cohort (flown Jan to Feb 2026, matured for Oct 2026): commercial c1 ($1,000 paid) and c3 (unpaid), Medicaid c2 ($200 paid).
    // October: commercial 2 (Rotor), Medicaid 2 (Fixed). September: commercial 3 (two Rotor, one Fixed), Medicaid 1 (Fixed).
    private static readonly SeedTrip[] Trips =
    [
        new(101, "Rotor Wing", null, "AETNA", "Insurance", "2026-01-15", 1, Rotor),
        new(102, "Rotor Wing", null, "OHP", "Medicaid MCO", "2026-02-01", 1, FixedLegacy),
        new(103, "Rotor Wing", null, "AETNA", "Insurance", "2026-02-02", 1, FixedLegacy),
        new(1, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-03", 1, Rotor),
        new(2, "Rotor Wing", null, "OHP", "Medicaid MCO", "2026-10-04", 1, FixedLegacy),
        new(3, "Rotor Wing", null, "OHP", "Medicaid MCO", "2026-10-05", 1, FixedLegacy),
        new(4, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-06", 1, Rotor),
        new(5, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-03", 1, Rotor),
        new(6, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-04", 1, FixedLegacy),
        new(7, "Rotor Wing", null, "AETNA", "Insurance", "2026-09-05", 1, Rotor),
        new(8, "Rotor Wing", null, "OHP", "Medicaid MCO", "2026-09-06", 1, FixedLegacy),
        new(9, "Rotor Wing", null, "AETNA", "Insurance", "2026-10-07", 1, "Other Provider - Base", Provider: 777),
    ];

    private static readonly SeedCash[] Cash = [new(1, 101, 2, 100000), new(2, 102, 1, 20000), new(3, 9, 9, 999999)];

    private async Task<(PayerMixFinancialEffectMetric Metric, PayerMixAnalysis Insight)> NewAsync()
    {
        IWarehouse warehouse = await CreateAsync(postgres, Trips, Cash, TestContext.Current.CancellationToken);
        var resolver = new FakeProviderResolver(null);
        var mix = new PayerMixMetric(warehouse, resolver);
        var rates = new CashPerTransportMetric(warehouse, resolver);
        return (new PayerMixFinancialEffectMetric(mix, rates), new PayerMixAnalysis(mix, rates));
    }

    [Fact]
    public async Task The_effect_is_the_share_move_times_matured_cash_per_transport_times_this_months_transports()
    {
        (PayerMixFinancialEffectMetric metric, _) = await NewAsync();

        PayerMixFinancialEffect effect = await metric.GetEffectAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        // Commercial 75% to 50% of 4 transports at 500 each (1,000 over 2): -500. Medicaid 25% to 50% at 200 each: +200.
        Assert.Equal((4, 4), (effect.Transports, effect.PriorTransports));
        Assert.Equal([("commercial", -500m), ("medicaid", 200m)], effect.Groups.Where(g => g.Effect is not null).Select(g => (g.Share.Key, g.Effect!.Value)));
        Assert.Equal(-300m, effect.TotalEffect);
    }

    [Fact]
    public async Task The_insight_reports_the_same_total_for_the_same_data()
    {
        (PayerMixFinancialEffectMetric metric, PayerMixAnalysis insight) = await NewAsync();

        Finding? finding = await insight.RunAsync(new InsightInput(2026, Oct, ServiceLineFilter.Air, null, [], []), TestContext.Current.CancellationToken);
        PayerMixFinancialEffect effect = await metric.GetEffectAsync(2026, Oct, ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(effect.TotalEffect, Assert.IsType<PayerMixFinding>(finding).Impact);
    }

    private static async Task<decimal?> TotalAsync(PayerMixFinancialEffectMetric metric, ReportingDivision? division) =>
        (await metric.GetEffectAsync(2026, Oct, ServiceLineFilter.Air, null, division, TestContext.Current.CancellationToken)).TotalEffect;

    private static async Task<int> TransportsAsync(PayerMixFinancialEffectMetric metric, ReportingDivision? division) =>
        (await metric.GetEffectAsync(2026, Oct, ServiceLineFilter.Air, null, division, TestContext.Current.CancellationToken)).Transports;

    [Fact]
    public async Task A_division_narrows_the_figures_and_the_divisions_of_the_provider_add_up_to_no_division()
    {
        (PayerMixFinancialEffectMetric metric, _) = await NewAsync();

        int all = await TransportsAsync(metric, null);
        int rotor = await TransportsAsync(metric, RotorDivision);
        int fixedTrips = await TransportsAsync(metric, FixedDivision);

        Assert.Equal(4, all);
        Assert.Equal((2, 2), (rotor, fixedTrips));
        // The matured cash per transport is the division's own: Rotor has only the paid commercial transport, Fixed only the paid Medicaid one and an unpaid commercial one.
        Assert.Equal(0m, await TotalAsync(metric, RotorDivision));
        Assert.Equal(200m, await TotalAsync(metric, FixedDivision));
        Assert.NotEqual(await TotalAsync(metric, null), await TotalAsync(metric, RotorDivision));
    }

    [Theory]
    [InlineData("Other Provider - Base")]
    [InlineData("Mercy Flights - Rotor - Old AR 2019")]
    [InlineData("x' OR '1'='1")]
    [InlineData("")]
    public async Task A_division_name_that_is_not_the_providers_matches_nothing_and_is_only_ever_a_bound_value(string name)
    {
        (PayerMixFinancialEffectMetric metric, _) = await NewAsync();

        int total = await TransportsAsync(metric, new ReportingDivision("k", name));

        Assert.Equal(name.Length == 0 ? await TransportsAsync(metric, null) : 0, total);
    }

    [Fact]
    public async Task A_month_with_no_transports_gives_no_effect_instead_of_a_made_up_number()
    {
        (PayerMixFinancialEffectMetric metric, _) = await NewAsync();

        PayerMixFinancialEffect effect = await metric.GetEffectAsync(2026, new ApPeriod(2024, 1), ServiceLineFilter.Air, null, null, TestContext.Current.CancellationToken);

        Assert.Null(effect.TotalEffect);
        Assert.Equal((0, 0), (effect.Transports, effect.PriorTransports));
    }
}
