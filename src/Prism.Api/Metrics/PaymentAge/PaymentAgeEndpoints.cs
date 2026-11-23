using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Net cash by payer group and payment age.</summary>
internal static class PaymentAgeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/payment-age", PaymentAgeRoute)
            .WithName("GetPaymentAge")
            .WithSummary("Net cash by payer and payment age")
            .WithDescription(
                "Net cash for `period` (`yyyyMM`, default the newest period with net cash data) and the twelve months before it, by payer group, posting month and payment age. "
                + "Payment age is the number of months between the transport's date of service and the month the cash posted, so 0 is cash for a transport flown that month, and ages above 24 are counted at 24. "
                + "Net cash is payments less refunds by posting month, without the Old AR import, as in the breakdown. Cash for transports with no date of service is left out, so the cells can add up to less than the breakdown. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using the `key` the breakdown route returns (for example `fixed-wing`). "
                + "Aggregates only: no patient, claim or payer names. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<PaymentAgeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> PaymentAgeRoute(
        [AsParameters] PayerMixRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IPaymentAgeMetric metric,
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
        string? through = await access.DataThroughAsync(id, IPaymentAgeMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new PaymentAgeResponse(MetricOf(IPaymentAgeMetric.Definition), providerId, null, ServiceLineName(filter), groupKey, []) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        PaymentAge age = await metric.GetPaymentAgeAsync(id, chosen, filter, groupKey, division, cancellationToken);
        PaymentAgeResponse response = new(
            MetricOf(age.Metric),
            providerId,
            age.Period.ToString(),
            ServiceLineName(age.ServiceLine),
            age.GroupKey,
            age.Cells.Select(c => new PaymentAgeCellResponse(c.PayerKey, c.PayerName, c.PostingPeriod.ToString(), c.AgeMonths, c.Cash, c.Transports)).ToArray());
        return Results.Ok(response with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }
}

/// <summary>
/// Net cash by payer group, posting month and payment age for <c>Period</c> and the twelve months before it. <c>Group</c> is set when the figures are
/// for one aircraft type or level of service.
/// </summary>
public sealed record PaymentAgeResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string ServiceLine,
    string? Group,
    IReadOnlyList<PaymentAgeCellResponse> Cells)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// Cash (<c>Cash</c>, USD) posted in <c>PostingPeriod</c> for <c>Transports</c> transports of payer group <c>PayerKey</c> that were <c>AgeMonths</c> months old
/// when paid. <c>PayerKey</c> is stable, <c>PayerName</c> is for display.
/// </summary>
public sealed record PaymentAgeCellResponse(string PayerKey, string PayerName, string PostingPeriod, int AgeMonths, decimal Cash, int Transports);
