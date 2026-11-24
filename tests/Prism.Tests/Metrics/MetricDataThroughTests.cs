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
    [InlineData("clean_claim_rate", ServiceLineFilter.Air, DataThroughSource.CleanClaims)]
    [InlineData("payer_mix", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("transport_volume", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("payment_age", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("cash_per_transport_by_payer", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("net_cash_breakdown", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("net_cash_by_group", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("transport_mix_by_call_type", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("aircraft_performance", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("base_analysis", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("loaded_miles_by_aircraft", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("payer_mix_financial_effect", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("transports_by_pickup_location", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("cash_contribution_by_aircraft", ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData("cash_contribution_by_division", ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    [InlineData("cash_contribution_by_payer", ServiceLineFilter.Ground, DataThroughSource.TripDetail)]
    [InlineData(MetricDataThrough.WhatChanged, ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    [InlineData(MetricDataThrough.ClaimAttention, ServiceLineFilter.Both, DataThroughSource.TripDetail)]
    public void A_metric_reads_the_load_date_of_its_own_table(string key, ServiceLineFilter line, DataThroughSource expected) =>
        Assert.Equal([expected], MetricDataThrough.SourcesFor(key, line));

    [Theory]
    [InlineData(ServiceLineFilter.Both, DataThroughSource.NetCollectionsKpi)]
    [InlineData(ServiceLineFilter.Air, DataThroughSource.TripDetail)]
    public void The_projection_reads_the_forecast_and_the_actual_series_it_compares_with(ServiceLineFilter line, DataThroughSource actual) =>
        Assert.Equal([DataThroughSource.CashForecast, actual], MetricDataThrough.SourcesFor("net_cash_projection", line));

    [Fact]
    public void Every_available_placeable_metric_has_a_source()
    {
        Assert.All(MetricCatalog.Entries.Where(e => e.CanBePlaced), e =>
            Assert.NotEmpty(MetricDataThrough.SourcesFor(e.Key, ServiceLineFilter.Both)));
    }
}
