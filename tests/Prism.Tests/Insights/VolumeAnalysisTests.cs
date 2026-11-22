using Prism.Application.Insights;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

public sealed class VolumeAnalysisTests
{
    private static readonly ApPeriod Oct = new(2026, 10);
    private static readonly ApPeriod Sep = new(2026, 9);

    private static InsightInput Input(MonthlyCash[] history, params Finding[] earlier) => new(1, Oct, ServiceLineFilter.Air, "fixed-wing", history, earlier);

    private static Task<Finding?> RunAsync(InsightInput input) => new VolumeAnalysis().RunAsync(input, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Fewer_transports_are_priced_at_last_months_cash_per_transport_and_watched()
    {
        Finding? finding = await RunAsync(Input([new(Sep, 100_000m, 100), new(Oct, 80_000m, 80)]));

        VolumeFinding volume = Assert.IsType<VolumeFinding>(finding);
        Assert.Equal((-20_000m, 100m, 80m, FindingLabel.Watch), (volume.Impact, volume.TransportsFrom, volume.TransportsTo, volume.Label));
    }

    [Fact]
    public async Task More_transports_are_for_information()
    {
        Finding? finding = await RunAsync(Input([new(Sep, 100_000m, 100), new(Oct, 120_000m, 120)]));

        VolumeFinding volume = Assert.IsType<VolumeFinding>(finding);
        Assert.Equal((20_000m, FindingLabel.Info), (volume.Impact, volume.Label));
    }

    [Fact]
    public async Task A_batch_of_aged_payments_is_taken_out_of_both_months_first()
    {
        // September has an 85,000 batch from 14 transports. Without it September is 100 transports at 1,000 each.
        var aged = new AgedPaymentsFinding(-85_000m, Sep, 0m, 85_000m, 0m, 14m, 14);

        Finding? finding = await RunAsync(Input([new(Sep, 185_000m, 114), new(Oct, 80_000m, 80)], aged));

        VolumeFinding volume = Assert.IsType<VolumeFinding>(finding);
        Assert.Equal((-20_000m, 100m, 80m), (volume.Impact, volume.TransportsFrom, volume.TransportsTo));
    }

    [Fact]
    public async Task Without_a_prior_month_or_a_change_there_is_nothing_to_say()
    {
        Assert.Null(await RunAsync(Input([new(Oct, 80_000m, 80)])));
        Assert.Null(await RunAsync(Input([new(Sep, 100_000m, 80), new(Oct, 90_000m, 80)])));
        Assert.Null(await RunAsync(Input([new(Sep, 0m, 0), new(Oct, 90_000m, 80)])));
    }
}
