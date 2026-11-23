using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Tests.Metrics;

/// <summary>Each Metric's Data Through comes from the table it reads.</summary>
public sealed class MetricDataThroughTests
{
    [Theory]
    [InlineData("net_collections", ServiceLineFilter.Both, DataThroughSource.NetCollectionsKpi)]
    [InlineData("net_collections", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("net_collections", ServiceLineFilter.Ground, DataThroughSource.TripDetail)]
    [InlineData("net_cash_projection", ServiceLineFilter.Both, DataThroughSource.CashForecast)]
    [InlineData("clean_claim_rate", ServiceLineFilter.Air, DataThroughSource.CleanClaims)]
    [InlineData("payer_mix", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("transport_volume", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("payment_age", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("cash_per_transport_by_payer", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("net_cash_breakdown", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("net_cash_by_group", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData(MetricDataThrough.WhatChanged, ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData(MetricDataThrough.ClaimAttention, ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    public void A_metric_reads_the_load_date_of_its_own_table(string key, ServiceLineFilter line, DataThroughSource expected) =>
        Assert.Equal([expected], MetricDataThrough.SourcesFor(key, line));

    [Fact]
    public void Every_available_placeable_metric_has_a_source()
    {
        Assert.All(MetricCatalog.Entries.Where(e => e.CanBePlaced), e =>
            Assert.NotEmpty(MetricDataThrough.SourcesFor(e.Key, ServiceLineFilter.Both)));
    }
}
