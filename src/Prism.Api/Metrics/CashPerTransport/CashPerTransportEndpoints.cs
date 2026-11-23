using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Matured net cash per transport by payer group.</summary>
internal static class CashPerTransportEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/cash-per-transport", CashPerTransportRoute)
            .WithName("GetCashPerTransport")
            .WithSummary("Matured net cash per transport by payer")
            .WithDescription(
                "What a transport earns by primary payer group, for transports old enough to have been paid. The cohorts are the twelve date-of-service months that ended at least six months before `period` "
                + "(`yyyyMM`, default the newest period with net cash data), returned as `cohortFrom` and `cohortThrough`. For each payer group: the `transports` in the cohorts (paid or not), the net `cash` posted for them up to and including `period` "
                + "(payments less refunds, without the Old AR import), and `cashPerTransport`, which is null when the group has no transports. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using the `key` the breakdown route returns (for example `fixed-wing`). "
                + "Every payer group is listed in a fixed order, with zeros. Aggregates only: no patient, claim or payer names. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<CashPerTransportResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> CashPerTransportRoute(
        [AsParameters] PayerMixRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ICashPerTransportMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? groupKey, string? divisionKey) = (request.ProviderId, request.Period, request.ServiceLine, request.Group, request.Division);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (serviceLine is not null && !TryParseServiceLine(serviceLine, out filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        if (groupKey is not null && !GroupKeyPattern().IsMatch(groupKey))
        {
            return Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.");
        }

        DivisionSelection selection = await access.ResolveDivisionAsync(id, divisionKey, cancellationToken);
        if (selection.Error is { } divisionError)
        {
            return Invalid("division", divisionError);
        }

        ReportingDivision? division = selection.Division;
        string? through = await access.DataThroughAsync(id, ICashPerTransportMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new CashPerTransportResponse(MetricOf(ICashPerTransportMetric.Definition), providerId, null, null, null, ServiceLineName(filter), groupKey, []) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        CashPerTransport result = await metric.GetCashPerTransportAsync(id, chosen, filter, groupKey, division, cancellationToken);
        CashPerTransportResponse response = new(
            MetricOf(result.Metric),
            providerId,
            result.Period.ToString(),
            result.CohortFrom.ToString(),
            result.CohortThrough.ToString(),
            ServiceLineName(result.ServiceLine),
            result.GroupKey,
            result.Payers.Select(p => new PayerCashPerTransportResponse(p.Key, p.Name, p.Transports, p.Cash, p.CashPerTransport)).ToArray());
        return Results.Ok(response with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }
}

/// <summary>
/// Matured net cash per transport by payer group for <c>Period</c>. The cohorts are the transports flown from <c>CohortFrom</c> to <c>CohortThrough</c>.
/// <c>Group</c> is set when the figures are for one aircraft type or level of service.
/// </summary>
public sealed record CashPerTransportResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? CohortFrom,
    string? CohortThrough,
    string ServiceLine,
    string? Group,
    IReadOnlyList<PayerCashPerTransportResponse> Payers)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>A payer group: <c>Key</c> is stable, <c>Name</c> is for display. <c>CashPerTransport</c> (USD) is null when there are no transports.</summary>
public sealed record PayerCashPerTransportResponse(string Key, string Name, int Transports, decimal Cash, decimal? CashPerTransport);
