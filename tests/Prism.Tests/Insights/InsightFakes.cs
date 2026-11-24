using Prism.Application.Warehouse;
using Prism.Application.Metrics;

namespace Prism.Tests.Insights;

/// <summary>Stand-ins for the Catalog Metrics the analyses read, with the data a test hands them.</summary>
internal static class InsightFakes
{
    internal sealed class PaymentAgeFake(IReadOnlyList<PaymentAgeCell> cells) : IPaymentAgeMetric
    {
        public Task<PaymentAge> GetPaymentAgeAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentAge(IPaymentAgeMetric.Definition, period, IPaymentAgeMetric.MonthsBefore, serviceLine, groupKey, cells));
    }

    internal sealed class PayerMixFake(Func<ApPeriod, IReadOnlyList<PayerMixShare>> shares) : IPayerMixMetric
    {
        public List<ApPeriod> Calls { get; } = [];

        public Task<PayerMix> GetPayerMixAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Calls.Add(period);
            return Task.FromResult(new PayerMix(IPayerMixMetric.Definition, period, period.AddMonths(-1), serviceLine, groupKey, null, shares(period)));
        }
    }

    internal sealed class CashPerTransportFake(IReadOnlyList<PayerCashPerTransport> payers) : ICashPerTransportMetric
    {
        public Task<CashPerTransport> GetCashPerTransportAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken) =>
            Task.FromResult(new CashPerTransport(ICashPerTransportMetric.Definition, period, period.AddMonths(-17), period.AddMonths(-6), serviceLine, groupKey, payers));
    }

    internal sealed class NetCashByGroupFake(IReadOnlyList<NetCashByGroupPoint> points) : INetCashByGroupMetric
    {
        public Task<NetCashByGroup> GetNetCashByGroupAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken) =>
            Task.FromResult(new NetCashByGroup(INetCashByGroupMetric.Definition, period, INetCashByGroupMetric.MonthsBefore, serviceLine, points));
    }

    /// <summary>A net cash breakdown built from vehicle rows: the groups are those rows added up, as the real Metric does. Records what it was asked for.</summary>
    internal sealed class BreakdownFake(IReadOnlyList<NetCashBreakdownRow> rows) : INetCashBreakdownMetric
    {
        public List<(int ProviderId, ApPeriod Period, ServiceLineFilter Line, ReportingDivision? Division)> Calls { get; } = [];

        public Task<NetCashBreakdown> GetBreakdownAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken)
        {
            Calls.Add((providerId, period, serviceLine, division));
            NetCashBreakdownGroup[] groups =
            [
                .. rows.GroupBy(r => (r.GroupKey, r.GroupName, r.IsAir))
                    .Select(g => new NetCashBreakdownGroup(g.Key.GroupKey, g.Key.GroupName, g.Key.IsAir, g.Aggregate(BreakdownFigures.None, (sum, r) => sum.Plus(r.Current)), g.Aggregate(BreakdownFigures.None, (sum, r) => sum.Plus(r.Prior)))),
            ];
            return Task.FromResult(new NetCashBreakdown(INetCashBreakdownMetric.Definition, period, period.AddMonths(-1), serviceLine, false, groups, rows));
        }
    }
}
