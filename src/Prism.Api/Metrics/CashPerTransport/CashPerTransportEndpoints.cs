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
                + "Every payer group is listed in a fixed order, with zeros. `overall` is the matured net cash per transport across all payer groups for the same cohorts, `serviceLine` and `division`, without the `group` filter; each payer group has `differenceFromOverall` (USD) and `percentFromOverall` (percent units, null when overall is zero or missing). `cohorts` names the service months (`firstServiceMonth` to `lastServiceMonth`, 12 months matured by 6) and the payment period (`cashThroughPeriod`) used. Aggregates only: no patient, claim or payer names. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
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
            result.Payers.Select(p => new PayerCashPerTransportResponse(p.Key, p.Name, p.Transports, p.Cash, p.CashPerTransport, p.DifferenceFrom(result.Overall?.CashPerTransport), p.PercentFrom(result.Overall?.CashPerTransport))).ToArray())
        {
            Overall = result.Overall is { } overall ? new OverallCashPerTransportResponse(overall.Transports, overall.Cash, overall.CashPerTransport) : null,
            Cohorts = new CashPerTransportCohortsResponse(result.CohortFrom.ToString(), result.CohortThrough.ToString(), result.Period.ToString()),
        };
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
    /// <summary>The all-payer cash per transport for the same cohorts, Service Line and Division, without the <c>group</c> filter. Null when there is no period.</summary>
    public OverallCashPerTransportResponse? Overall { get; init; }

    /// <summary>The service period and payment period behind the figures. Null when there is no period.</summary>
    public CashPerTransportCohortsResponse? Cohorts { get; init; }

    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// A payer group: <c>Key</c> is stable, <c>Name</c> is for display. <c>CashPerTransport</c> (USD) is null when there are no transports.
/// <c>DifferenceFromOverall</c> (USD) is this group's figure minus <c>overall.cashPerTransport</c>, and <c>PercentFromOverall</c> that difference in percent units of the overall figure
/// (one decimal). The difference is null when the group or the overall figure has no transports, and the percent is also null when the overall figure is zero.
/// </summary>
public sealed record PayerCashPerTransportResponse(string Key, string Name, int Transports, decimal Cash, decimal? CashPerTransport, decimal? DifferenceFromOverall, decimal? PercentFromOverall);

/// <summary>Transports, net cash and cash per transport (USD, null when no transports) across every payer group, never narrowed to one aircraft type or level of service.</summary>
public sealed record OverallCashPerTransportResponse(int Transports, decimal Cash, decimal? CashPerTransport);

/// <summary>The service months of the cohorts (<c>FirstServiceMonth</c> to <c>LastServiceMonth</c>, <c>yyyyMM</c>) and the last posting month whose cash is counted (<c>CashThroughPeriod</c>).</summary>
public sealed record CashPerTransportCohortsResponse(string FirstServiceMonth, string LastServiceMonth, string CashThroughPeriod);
