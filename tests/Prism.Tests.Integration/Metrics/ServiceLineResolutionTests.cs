using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Prism.Api.Metrics;
using Prism.Application.Metrics;

namespace Prism.Tests.Integration.Metrics;

/// <summary>The shared <c>serviceLine</c> rule of an air-only, ground-only or both Metric, and the 400 it answers with.</summary>
public sealed class ServiceLineResolutionTests
{
    private static MetricCatalogEntry Entry(MetricAppliesTo appliesTo) =>
        MetricCatalog.Find("payer_mix")! with { Context = MetricCatalog.Find("payer_mix")!.Context with { AppliesTo = appliesTo } };

    [Theory]
    [InlineData(MetricAppliesTo.Air, null, ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Air, "all", ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Air, "air", ServiceLineFilter.Air)]
    [InlineData(MetricAppliesTo.Ground, null, ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Ground, "all", ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Ground, "ground", ServiceLineFilter.Ground)]
    [InlineData(MetricAppliesTo.Both, "all", ServiceLineFilter.Both)]
    [InlineData(MetricAppliesTo.Both, "ground", ServiceLineFilter.Ground)]
    public void All_and_the_metrics_own_line_are_accepted(MetricAppliesTo appliesTo, string? raw, ServiceLineFilter expected)
    {
        IResult? error = MetricEndpointSupport.ResolveServiceLine(Entry(appliesTo), raw, appliesTo == MetricAppliesTo.Ground ? ServiceLineFilter.Ground : ServiceLineFilter.Air, out ServiceLineFilter filter);

        Assert.Null(error);
        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData(MetricAppliesTo.Air, "ground", "air only")]
    [InlineData(MetricAppliesTo.Ground, "air", "ground only")]
    [InlineData(MetricAppliesTo.Both, "ZZ-MARKER-ZZ", "all, air or ground")]
    public void The_other_line_or_a_bad_value_is_a_validation_problem_that_echoes_nothing(MetricAppliesTo appliesTo, string raw, string phrase)
    {
        IResult? error = MetricEndpointSupport.ResolveServiceLine(Entry(appliesTo), raw, ServiceLineFilter.Air, out _);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(error);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        string json = JsonSerializer.Serialize(((HttpValidationProblemDetails)problem.ProblemDetails).Errors);
        Assert.Contains(phrase, json, StringComparison.Ordinal);
        Assert.DoesNotContain(raw, json, StringComparison.Ordinal);
    }
}
