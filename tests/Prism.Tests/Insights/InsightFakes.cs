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
}
