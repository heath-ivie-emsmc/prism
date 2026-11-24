using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>The expected cash effect of a shift in payer mix.</summary>
internal static class PayerMixFinancialEffectEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/payer-mix-financial-effect", Route)
            .WithName("GetPayerMixFinancialEffect")
            .WithSummary("Expected cash effect of the payer mix")
            .WithDescription(
                "The expected effect on cash of the change in payer mix between `period` (`yyyyMM`, default the newest period with net cash data) and the month before. For each payer group the effect is its change in share of transports (by date of service), times what a transport of that group has earned once matured (`cashPerTransport`, the same measure as the cash per transport route), times the transports of `period`. "
                + "It is an expected effect, not posted cash (`basis` is `expected`): payer mix is by date of service and cash posts later. `estimatedEffect` is the sum over the payer groups and `recurringMonthlyEffect` is the same number, on the assumption that the shift persists every month. "
                + "A group's `cashPerTransport` and `effect` are null when it had no matured transports; `estimatedEffect` and `recurringMonthlyEffect` are null when no group has an effect or either month had no transports. `share`, `priorShare` are fractions (0 to 1) and `changePoints` is in percentage points. "
                + "`cohorts` gives the date-of-service months behind the matured cash per transport. The Insight on the dashboard uses the same calculation. `serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using a `key` from the breakdown route. `division` limits it to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<PayerMixFinancialEffectResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static async Task<IResult> Route(
        [AsParameters] ScopeRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IPayerMixFinancialEffectMetric metric,
        CancellationToken cancellationToken)
    {
        MetricCatalogEntry entry = MetricCatalog.Find(IPayerMixFinancialEffectMetric.Definition.Key)!;
        (ResolvedMetric? resolved, IResult? failure) = await MetricRequestResolver.ResolveAsync(entry, user, access, periods, request.Values, ServiceLineFilter.Air, cancellationToken);
        if (resolved is null)
        {
            return failure!;
        }

        if (resolved.Period is not { } period)
        {
            return Results.Ok(new PayerMixFinancialEffectResponse(MetricOf(IPayerMixFinancialEffectMetric.Definition), request.ProviderId, null, null, ServiceLineName(resolved.ServiceLine), resolved.Group, "expected", 0, 0, null, [], null, null)
                { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
        }

        PayerMixFinancialEffect result = await metric.GetEffectAsync(resolved.Id, period, resolved.ServiceLine, resolved.Group, resolved.Division, cancellationToken);
        decimal? total = result.TotalEffect is { } t ? Math.Round(t, 2, MidpointRounding.AwayFromZero) : null;
        PayerMixFinancialEffectResponse response = new(
            MetricOf(result.Metric),
            request.ProviderId,
            result.Period.ToString(),
            result.PriorPeriod.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            "expected",
            result.Transports,
            result.PriorTransports,
            new EffectCohortsResponse(result.CohortFrom.ToString(), result.CohortThrough.ToString(), result.Period.ToString()),
            [.. result.Groups.Select(g => new PayerEffectResponse(
                g.Share.Key,
                g.Share.Name,
                g.Share.Current,
                g.Share.Prior,
                MetricMath.Share(g.Share.Current, result.Transports),
                MetricMath.Share(g.Share.Prior, result.PriorTransports),
                MetricMath.ChangePoints(g.Share.Current, result.Transports, g.Share.Prior, result.PriorTransports),
                g.CashPerTransport is { } c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null,
                g.Effect is { } e ? Math.Round(e, 2, MidpointRounding.AwayFromZero) : null))],
            total,
            total);
        return Results.Ok(response with { DataThrough = resolved.Through, Division = resolved.Division?.Key, DivisionName = resolved.Division?.Name });
    }
}

/// <summary>
/// The expected effect of the payer mix on cash for <c>Period</c> against the month before. <c>Basis</c> is always <c>expected</c>: it is not posted cash. <c>EstimatedEffect</c> is the sum of the group effects (USD, null when it cannot be worked out)
/// and <c>RecurringMonthlyEffect</c> is the same number, which holds each month only if the shift persists.
/// </summary>
public sealed record PayerMixFinancialEffectResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    string Basis,
    int Transports,
    int PriorTransports,
    EffectCohortsResponse? Cohorts,
    IReadOnlyList<PayerEffectResponse> Groups,
    decimal? EstimatedEffect,
    decimal? RecurringMonthlyEffect)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>The date-of-service months (<c>yyyyMM</c>) behind the matured cash per transport, and the last posting month counted.</summary>
public sealed record EffectCohortsResponse(string FirstServiceMonth, string LastServiceMonth, string CashThroughPeriod);

/// <summary>
/// One payer group. <c>Share</c> and <c>PriorShare</c> are fractions (0 to 1, four decimals) of that month's transports, null when it had none. <c>ChangePoints</c> is the move in percentage points.
/// <c>CashPerTransport</c> is matured net cash per transport (USD), and <c>Effect</c> the expected effect on this month's cash (USD). Both are null when the group has no matured transports or either month had none.
/// </summary>
public sealed record PayerEffectResponse(string Key, string Name, int Transports, int PriorTransports, decimal? Share, decimal? PriorShare, decimal? ChangePoints, decimal? CashPerTransport, decimal? Effect);
