using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports by primary payer group.</summary>
internal static class PayerMixEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/payer-mix", PayerMixRoute)
            .WithName("GetPayerMix")
            .WithSummary("Transports by primary payer group")
            .WithDescription(
                "Transports by the primary payer's group for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, by date of service. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using the `key` the breakdown route returns (for example `fixed-wing`). "
                + "Groups are Commercial (insurance and third-party liability), Medicare (with Medicare Advantage), Medicaid (with Medicaid managed care), Other government, VA (VA, CHAMPVA and TRICARE payers by name), Facility and Patient responsibility. "
                + "`not-recorded` appears only when some transports have no payer. Counts only: no patient, claim or payer names. Each share has `links`: a `drill` link to the payers inside Commercial, Medicare, Medicaid, Other government and VA, issued by the server for this Provider and these filters, and none for the other groups. "
                + "`division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<PayerMixResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> PayerMixRoute(
        [AsParameters] PayerMixRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IPayerMixMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? groupKey, string? divisionKey) = (request.ProviderId, request.Period, request.ServiceLine, request.Group, request.Division);
        if (await access.ResolveAggregateAsync(user, providerId, cancellationToken) is not { } id)
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
        string? through = await access.DataThroughAsync(id, IPayerMixMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new PayerMixResponse(MetricOf(IPayerMixMetric.Definition), providerId, null, null, ServiceLineName(filter), groupKey, null, []) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        PayerMix mix = await metric.GetPayerMixAsync(id, chosen, filter, groupKey, division, cancellationToken);
        var links = new DrillLinkBuilder(MetricCatalog.Find(IPayerMixMetric.Definition.Key)!, user, providerId, new DrillContext(mix.Period.ToString(), mix.ServiceLine, groupKey, division?.Key));
        PayerMixResponse response = new(
            MetricOf(mix.Metric),
            providerId,
            mix.Period.ToString(),
            mix.PriorPeriod.ToString(),
            ServiceLineName(mix.ServiceLine),
            mix.GroupKey,
            mix.GroupName,
            mix.Shares.Select(s => new PayerMixShareResponse(s.Key, s.Name, s.Current, s.Prior, links.LinksFor(s.Key, s.Name))).ToArray());
        return Results.Ok(response with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }
}

/// <summary>Route and query values of the payer mix route.</summary>
internal readonly record struct PayerMixRequest(
    string ProviderId,
    string? Period,
    string? ServiceLine,
    string? Group,
    string? Division);

/// <summary>
/// Transports by primary payer group for <c>Period</c> and the month before it (<c>PriorPeriod</c>), by date of service. <c>Group</c> and <c>GroupName</c>
/// are set when the figures are for one aircraft type or level of service.
/// </summary>
public sealed record PayerMixResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    string? GroupName,
    IReadOnlyList<PayerMixShareResponse> Shares)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// A payer group: <c>Key</c> is stable, <c>Name</c> is for display, <c>Current</c> and <c>Prior</c> are transport counts. <c>Links</c> are the drills the server
/// offers for this group (<c>rel</c> <c>drill</c>, to the payers inside it). It is empty for Facility, Patient responsibility and Not recorded.
/// </summary>
public sealed record PayerMixShareResponse(string Key, string Name, int Current, int Prior, IReadOnlyList<LinkResponse> Links);
