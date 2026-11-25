using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Api.Metrics;

/// <summary>The written explanation of what changed in a Provider's net cash, built from the Metric Catalog. Aggregates only, for the dashboard.</summary>
internal static class InsightsEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/insights")
            .RequireAuthorization(Permissions.DashboardView)
            .AddEndpointFilter(MetricCacheHeaders.Revalidate)
            .WithTags("Insights");

        group.MapGet("/what-changed", WhatChanged)
            .WithName("GetWhatChanged")
            .WithSummary("What changed in net cash against the month before")
            .WithDescription(
                "Explains the change in posted net cash for `period` (`yyyyMM`, default the newest period with net cash data) against the month before, for one aircraft type or level of service (`group`, a `key` from the breakdown route such as `fixed-wing`) "
                + "or the whole Service Line (`serviceLine` is `all`, `air` (default) or `ground`). `drivers` are the findings that explain part of the posted change, largest first, and with `remainder` they add up to `delta`. "
                + "`expected` are findings on another basis, such as what a shift in payer mix is expected to do to cash as claims are paid, or a shift of transports between Reporting Divisions that is another view of a change already explained by aircraft type, and are not part of `delta`. "
                + "The findings are `aged-payments`, `volume`, `payer-mix`, `aircraft-mix` (transports moving between aircraft types or levels of service that earn different amounts, a driver) and `base-mix` (the same between Reporting Divisions: a driver only when no aircraft mix explains the change, otherwise listed in `expected`). "
                + "`remainder.judgment` is `withinNormalRange` only when the unexplained part is inside the usual month-to-month range of the last twelve months, `outsideNormalRange` when it is not, and `notEnoughHistory` when there are not twelve months to judge by. "
                + "`normalizedDeltaRatio` is the change with unusually high aged payments taken out of both months, or null when there were none. Every figure in the text comes from a finding. "
                + "`division` limits the whole explanation (the headline, every driver and the remainder) to one Reporting Division, using a `key` from the divisions route (an unknown key is a 400), so they still add up to `delta` within that division: aged payments, volume, payer mix and aircraft mix are worked out inside the division, "
                + "and `base-mix` is left out because it is a cut across divisions. Cash for an encounter with no Transport record or no Reporting Division belongs to no division. "
                + "Aggregates only: no patient, claim or payer names. Returns 204 when neither month has cash to explain, and 404 when the Provider is not one the caller may read in full.")
            .Produces<WhatChangedResponse>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    public static async Task<IResult> WhatChanged(
        [AsParameters] PayerMixRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IInsightService insights,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? groupKey, string? divisionKey) = (request.ProviderId, request.Period, request.ServiceLine, request.Group, request.Division);
        if (await access.ResolveAggregateAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (serviceLine is not null && !MetricEndpointSupport.TryParseServiceLine(serviceLine, out filter))
        {
            return MetricEndpointSupport.Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        if (groupKey is not null && !MetricEndpointSupport.GroupKeyPattern().IsMatch(groupKey))
        {
            return MetricEndpointSupport.Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.");
        }

        DivisionSelection selection = await access.ResolveDivisionAsync(id, divisionKey, cancellationToken);
        if (selection.Error is { } divisionError)
        {
            return MetricEndpointSupport.Invalid("division", divisionError);
        }

        ReportingDivision? division = selection.Division;
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.NoContent();
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(MetricEndpointSupport.ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return MetricEndpointSupport.Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        InsightResult? result = await insights.GetWhatChangedAsync(id, chosen, filter, groupKey, division, cancellationToken);
        if (result is null)
        {
            return Results.NoContent();
        }

        // The date comes from the shared reader, like every Metric response, not from the Insight's own computation.
        string? through = await access.DataThroughAsync(id, MetricDataThrough.WhatChanged, filter, cancellationToken);
        return Results.Ok(ToResponse(providerId, filter, result) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }

    private static WhatChangedResponse ToResponse(string providerId, ServiceLineFilter filter, InsightResult result)
    {
        InsightHeadline h = result.Insight.Headline;
        IReadOnlyList<InsightCard> cards = result.Narrative.Cards;
        return new WhatChangedResponse(
            providerId,
            h.Period.ToString(),
            h.PriorPeriod.ToString(),
            MetricEndpointSupport.ServiceLineName(filter),
            h.GroupKey,
            h.GroupName,
            null,
            result.Narrative.Headline,
            h.CashNow,
            h.CashPrior,
            h.Delta,
            h.DeltaRatio,
            result.Insight.NormalizedDeltaRatio,
            [.. cards.Where(c => c.Basis == FindingBasis.PostedCash).Select(Card)],
            [.. cards.Where(c => c.Basis != FindingBasis.PostedCash).Select(Card)],
            new RemainderResponse(result.Insight.Remainder.Amount, JudgmentName(result.Insight.Remainder.Judgment), result.Narrative.RemainderLine));
    }

    private static InsightCardResponse Card(InsightCard c) => new(c.Kind, LabelName(c.Label), c.Title, c.Body, c.Impact);

    private static string LabelName(FindingLabel label) => label switch
    {
        FindingLabel.Temporary => "temporary",
        FindingLabel.Structural => "structural",
        FindingLabel.Watch => "watch",
        _ => "info",
    };

    private static string JudgmentName(RemainderJudgment judgment) => judgment switch
    {
        RemainderJudgment.WithinNormalRange => "withinNormalRange",
        RemainderJudgment.OutsideNormalRange => "outsideNormalRange",
        _ => "notEnoughHistory",
    };
}

/// <summary>
/// What changed in posted net cash (USD) for <c>Period</c> against <c>PriorPeriod</c>. <c>DeltaRatio</c> is the change as a share of last month's cash (-0.22 is a 22% fall),
/// null when last month had none. <c>Headline</c> is the one-sentence summary. <c>DataThrough</c> is the date of the warehouse load, <c>yyyy-MM-dd</c>.
/// </summary>
public sealed record WhatChangedResponse(
    string ProviderId,
    string Period,
    string PriorPeriod,
    string ServiceLine,
    string? Group,
    string GroupName,
    string? DataThrough,
    string Headline,
    decimal CashNow,
    decimal CashPrior,
    decimal Delta,
    decimal? DeltaRatio,
    decimal? NormalizedDeltaRatio,
    IReadOnlyList<InsightCardResponse> Drivers,
    IReadOnlyList<InsightCardResponse> Expected,
    RemainderResponse Remainder)
{
    /// <summary>The <c>key</c> of the Reporting Division the whole explanation is limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// One finding. <c>Kind</c> is a stable key (`aged-payments`, `volume`, `payer-mix`). <c>Label</c> is `temporary`, `structural`, `watch` or `info`.
/// <c>Impact</c> is USD and signed: negative lowered cash, or is expected to.
/// </summary>
public sealed record InsightCardResponse(string Kind, string Label, string Title, string Body, decimal Impact);

/// <summary>The part of the change no driver explains (USD), how it compares with the usual month-to-month range, and the sentence for it.</summary>
public sealed record RemainderResponse(decimal Amount, string Judgment, string Text);
