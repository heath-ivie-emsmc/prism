namespace Prism.Application.Insights;

/// <summary>
/// How much of the change is more or fewer transports having cash posted, priced at last month's cash per transport. Any unusually high aged payments
/// found first are taken out of both months before this runs, so a one-off batch is not counted as a change in volume as well.
/// </summary>
public sealed class VolumeAnalysis : IInsightAnalysis
{
    public int Order => 20;

    public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
    {
        if (input.Current is not { } now || input.Prior is not { } before)
        {
            return null;
        }

        AgedPaymentsFinding? aged = (await input.FindingsBeforeAsync()).OfType<AgedPaymentsFinding>().FirstOrDefault();
        return Run(now, before, aged);
    }

    private static VolumeFinding? Run(MonthlyCash now, MonthlyCash before, AgedPaymentsFinding? aged)
    {
        decimal cashBefore = before.Cash - (aged?.ExcessPriorMonth ?? 0m);
        decimal transportsBefore = before.Transports - (aged?.ExcessTransportsPriorMonth ?? 0m);
        decimal transportsNow = now.Transports - (aged?.ExcessTransportsThisMonth ?? 0m);
        if (transportsBefore <= 0m || transportsNow < 0m || transportsNow == transportsBefore)
        {
            return null;
        }

        decimal impact = (transportsNow - transportsBefore) * (cashBefore / transportsBefore);
        return new VolumeFinding(impact < 0m ? FindingLabel.Watch : FindingLabel.Info, impact, transportsBefore, transportsNow);
    }
}
