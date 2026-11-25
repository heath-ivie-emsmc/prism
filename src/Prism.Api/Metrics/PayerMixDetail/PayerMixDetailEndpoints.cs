using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports by payer inside one payer group of the payer mix.</summary>
internal static class PayerMixDetailEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/payer-mix/{payerKey}", PayerMixDetailRoute)
            .WithName("GetPayerMixDetail")
            .WithSummary("Transports by payer within one payer group")
            .WithDescription(
                "The payers behind one group of the payer mix, reached from the `drill` link on a payer mix share. `payerKey` is `commercial`, `medicare`, `medicaid`, `other-gov` or `va`; "
                + "`patient`, `facility`, `not-recorded` and any other value are a 404, because those groups have no payers to compare. "
                + "`period` (`yyyyMM`, default the newest period with net cash data), `serviceLine` (`all`, `air` (default) or `ground`), `group` and `division` mean what they do on the payer mix route and give the same Transports, "
                + "so the rows add up to that group's share there. `payers` are the 10 payers with the most transports in the month and the month before, then one `Other` row for the rest. "
                + "A payer name is the warehouse's text, trimmed, with control characters removed and cut to 120 characters. Counts only: no patient or claim data. "
                + "An unknown `division` key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<PayerMixDetailResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    public static async Task<IResult> PayerMixDetailRoute(
        [AsParameters] PayerMixDetailRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IPayerMixDetailMetric metric,
        CancellationToken cancellationToken)
    {
        if (await access.ResolveAggregateAsync(user, request.ProviderId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        // Checked against a fixed list, never echoed: an unknown, oversize or garbage key is the same 404 as a group with no detail.
        if (!IPayerMixDetailMetric.IsDrillable(request.PayerKey))
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (request.ServiceLine is not null && !TryParseServiceLine(request.ServiceLine, out filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        if (request.Group is not null && !GroupKeyPattern().IsMatch(request.Group))
        {
            return Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.");
        }

        DivisionSelection selection = await access.ResolveDivisionAsync(id, request.Division, cancellationToken);
        if (selection.Error is { } divisionError)
        {
            return Invalid("division", divisionError);
        }

        ReportingDivision? division = selection.Division;
        string? through = await access.DataThroughAsync(id, IPayerMixDetailMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (request.Period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new PayerMixDetailResponse(MetricOf(IPayerMixDetailMetric.Definition), request.ProviderId, request.Group, null, new PayerGroupResponse(request.PayerKey, PayerGroupName(request.PayerKey)), null, null, ServiceLineName(filter), [])
                {
                    DataThrough = through,
                    Division = division?.Key,
                    DivisionName = division?.Name,
                });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(request.Period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        PayerMixDetail detail = await metric.GetDetailAsync(id, chosen, filter, request.Group, request.PayerKey, division, cancellationToken);
        return Results.Ok(new PayerMixDetailResponse(
            MetricOf(detail.Metric),
            request.ProviderId,
            detail.GroupKey,
            detail.GroupName,
            new PayerGroupResponse(detail.PayerGroupKey, detail.PayerGroupName),
            detail.Period.ToString(),
            detail.PriorPeriod.ToString(),
            ServiceLineName(detail.ServiceLine),
            [.. detail.Payers.Select(p => new PayerMixDetailPayerResponse(p.Name, p.Current, p.Prior))])
        {
            DataThrough = through,
            Division = division?.Key,
            DivisionName = division?.Name,
        });
    }

    private static string PayerGroupName(string key) => key switch
    {
        "commercial" => "Commercial",
        "medicare" => "Medicare",
        "medicaid" => "Medicaid",
        "other-gov" => "Other government",
        "va" => "VA",
        _ => key,
    };
}

/// <summary>Route and query values of the payer mix detail route.</summary>
internal readonly record struct PayerMixDetailRequest(
    string ProviderId,
    string PayerKey,
    string? Period,
    string? ServiceLine,
    string? Group,
    string? Division);

/// <summary>The payer group the detail is for: <c>Key</c> is stable, <c>Name</c> is for display.</summary>
public sealed record PayerGroupResponse(string Key, string Name);

/// <summary>
/// The payers inside one payer group of the payer mix for <c>Period</c> and the month before it (<c>PriorPeriod</c>), by date of service.
/// <c>Group</c> and <c>GroupName</c> are set when the figures are for one aircraft type or level of service.
/// </summary>
public sealed record PayerMixDetailResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Group,
    string? GroupName,
    PayerGroupResponse PayerGroup,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    IReadOnlyList<PayerMixDetailPayerResponse> Payers)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>A payer: <c>Name</c> is the warehouse's text (or <c>Other</c> for the rest), <c>Current</c> and <c>Prior</c> are transport counts.</summary>
public sealed record PayerMixDetailPayerResponse(string Name, int Current, int Prior);
