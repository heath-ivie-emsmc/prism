namespace Prism.Application.Metrics;

/// <summary>Whether a Metric can be placed on a Saved View today.</summary>
public enum MetricStatus
{
    /// <summary>The API serves it.</summary>
    Available,

    /// <summary>On the roadmap. Shown in the palette as "coming soon" and rejected if a layout names it.</summary>
    Planned,
}

/// <summary>A group of Metrics in the palette.</summary>
public sealed record MetricCategory(string Key, string Name);

/// <summary>
/// One way a Metric can be drawn on a Saved View. Sizes are the preset names (S, M, L) that suit it, and the bounds are in grid columns and rows
/// on the 12-column grid.
/// </summary>
public sealed record WidgetPresentation(
    string Key,
    string Name,
    string Icon,
    IReadOnlyList<string> Sizes,
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    bool IsDefault);

/// <summary>An option a widget exposes. <paramref name="Type"/> is <c>choice</c> or <c>toggle</c>. Choices and the default are strings, so a layout can store them as given.</summary>
public sealed record WidgetOption(
    string Key,
    string Name,
    string Type,
    IReadOnlyList<string> Choices,
    string Default,
    IReadOnlyList<string> AppliesTo);

/// <summary>
/// A Metric as the palette and the layout validator see it. Dimensions are the filters the Metric honors, from <see cref="MetricDimensions"/>.
/// A Metric that does not list <see cref="MetricDimensions.Division"/> ignores the Reporting Division filter, and the widget says so.
/// <paramref name="ClaimLevel"/> marks a Metric that would carry claim or patient detail: it cannot be Available until it has a PHI permission and an audit record.
/// </summary>
public sealed record MetricCatalogEntry(
    string Key,
    string Name,
    string Description,
    string Unit,
    string Category,
    MetricStatus Status,
    bool Placeable,
    bool ClaimLevel,
    IReadOnlyList<string> ServiceLines,
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<WidgetPresentation> Presentations,
    IReadOnlyList<WidgetOption> Options)
{
    /// <summary>True when a layout may name this Metric.</summary>
    public bool CanBePlaced => Status == MetricStatus.Available && Placeable;

    /// <summary>Where the Metric applies: Provider scoping, Service Lines, Reporting Division support, group-by dimensions and time grain.</summary>
    public MetricContext Context { get; init; } = new(true, ServiceLines, DivisionSupport.None, [], "period");

    /// <summary>How long a figure may be cached. A claim-level Metric is never cached.</summary>
    public MetricFreshness Freshness { get; init; } = new(ClaimLevel ? FreshnessPolicy.NoCache : FreshnessPolicy.DailyLoad);

    /// <summary>The key of the help entry for this Metric. Empty for a planned Metric.</summary>
    public string HelpKey { get; init; } = Status == MetricStatus.Available ? Key : string.Empty;

    /// <summary>Links from this Metric to another one: the target, and which of the target's dimensions the clicked value fills.</summary>
    public IReadOnlyList<MetricDrill> Drills { get; init; } = [];
}

/// <summary>Whether a Metric honors the Reporting Division filter.</summary>
public enum DivisionSupport
{
    /// <summary>Ignores the Reporting Division filter.</summary>
    None,

    /// <summary>Honors the Reporting Division filter.</summary>
    Supported,
}

/// <summary>How a figure is cached.</summary>
public enum FreshnessPolicy
{
    /// <summary>Cached against the Data Through watermark, so it changes once per daily load.</summary>
    DailyLoad,

    /// <summary>Never cached.</summary>
    NoCache,
}

/// <summary>
/// Which Service Lines a Metric is about. <see cref="Both"/> is the default. An <see cref="Air"/> Metric (tail numbers, aircraft) has air data only, and a
/// <see cref="Ground"/> Metric has ground data only. The rule for <see cref="MetricContext.ServiceLines"/> follows from it: <c>both</c> lists air, ground and both,
/// <c>air</c> lists air and both, <c>ground</c> lists ground and both.
/// </summary>
public enum MetricAppliesTo
{
    /// <summary>Air and ground. The Service Line filter splits it.</summary>
    Both,

    /// <summary>Air only. <c>serviceLine=air</c> and <c>all</c> both mean air, and <c>ground</c> is refused.</summary>
    Air,

    /// <summary>Ground only. <c>serviceLine=ground</c> and <c>all</c> both mean ground, and <c>air</c> is refused.</summary>
    Ground,
}

/// <summary>Where a Metric applies. <paramref name="TimeGrain"/> is <c>period</c> or <c>month</c>. <paramref name="AppliesTo"/> says whether the Metric is air only, ground only or both.</summary>
public sealed record MetricContext(
    bool ProviderScoped,
    IReadOnlyList<string> ServiceLines,
    DivisionSupport DivisionSupport,
    IReadOnlyList<string> GroupDimensions,
    string TimeGrain,
    MetricAppliesTo AppliesTo = MetricAppliesTo.Both);

/// <summary>The Service Line a Metric answers for, given the one asked for and what the Metric is about.</summary>
public static class ServiceLineScope
{
    /// <summary>
    /// The filter to run: <paramref name="requested"/> for a Metric about both lines, and the Metric's own line for an air-only or ground-only Metric when
    /// <c>all</c> or that line was asked for. Null when the Metric has no data for the line asked for (ground on an air-only Metric, air on a ground-only one).
    /// </summary>
    public static ServiceLineFilter? Effective(MetricAppliesTo appliesTo, ServiceLineFilter requested) => (appliesTo, requested) switch
    {
        (MetricAppliesTo.Both, _) => requested,
        (MetricAppliesTo.Air, ServiceLineFilter.Ground) => null,
        (MetricAppliesTo.Air, _) => ServiceLineFilter.Air,
        (MetricAppliesTo.Ground, ServiceLineFilter.Air) => null,
        _ => ServiceLineFilter.Ground,
    };

    /// <summary>The Service Line filter values a Metric accepts, in catalog words: <c>air</c> and <c>both</c>, <c>ground</c> and <c>both</c>, or all three.</summary>
    public static IReadOnlyList<string> LinesFor(MetricAppliesTo appliesTo) => appliesTo switch
    {
        MetricAppliesTo.Air => ["air", "both"],
        MetricAppliesTo.Ground => ["ground", "both"],
        _ => ["air", "ground", "both"],
    };
}

/// <summary>The caching policy of a Metric.</summary>
public sealed record MetricFreshness(FreshnessPolicy Policy);

/// <summary>A link from a Metric to another: <paramref name="Rel"/> names the relation, and the target binds one of its dimensions to the clicked value.</summary>
public sealed record MetricDrill(string Rel, string TargetKey, string BindsDimension);

/// <summary>The filter names a Metric can list.</summary>
public static class MetricDimensions
{
    public const string Period = "period";
    public const string Months = "months";
    public const string ServiceLine = "serviceLine";
    public const string Group = "group";
    public const string Division = "division";

    /// <summary>The payer group a drill binds in the target's path (see <see cref="MetricDrill"/>). Not a filter a Saved View sets.</summary>
    public const string PayerGroup = "payerGroup";
}

/// <summary>
/// Everything a Saved View may show, in one place: the Metrics the API serves and the ones still to come. The palette, the layout validator and the
/// widget option forms all read this, so a new Metric or option is added here and nowhere else. It is also the seam the semantic API replaces later.
/// </summary>
public static class MetricCatalog
{
    private static readonly string[] AllLines = ["air", "ground", "both"];
    private static readonly string[] Sizes = ["S", "M", "L"];
    private static readonly string[] MediumUp = ["M", "L"];
    private static readonly string[] PeriodAndLine = [MetricDimensions.Period, MetricDimensions.ServiceLine];

    // Metrics that read reporting.trip_detail_snapshot honor the Reporting Division. Each one listed here needs an integration test (see MetricCatalogTests).
    private static readonly string[] SeriesAndDivision = [MetricDimensions.Period, MetricDimensions.Months, MetricDimensions.ServiceLine, MetricDimensions.Division];
    private static readonly string[] PeriodLineAndDivision = [MetricDimensions.Period, MetricDimensions.ServiceLine, MetricDimensions.Division];
    private static readonly string[] PeriodGroupAndDivision = [MetricDimensions.Period, MetricDimensions.ServiceLine, MetricDimensions.Group, MetricDimensions.Division];
    private static readonly string[] PayerMixDetailDimensions = [MetricDimensions.Period, MetricDimensions.ServiceLine, MetricDimensions.Group, MetricDimensions.Division, MetricDimensions.PayerGroup];
    private static readonly string[] TrendGroupAndDivision = [MetricDimensions.Period, MetricDimensions.Months, MetricDimensions.ServiceLine, MetricDimensions.Group, MetricDimensions.Division];
    private static readonly string[] PeriodAndDivision = [MetricDimensions.Period, MetricDimensions.Division];
    private static readonly string[] PeriodAndMonths = [MetricDimensions.Period, MetricDimensions.Months];
    private static readonly string[] MonthChoices = ["6", "12"];

    private static readonly WidgetPresentation Table = new("table", "Table", "table_rows", MediumUp, 6, 3, 12, 6, false);
    private static readonly WidgetPresentation Tile = new("tile", "Tile", "pin", Sizes, 3, 2, 6, 3, false);


    public static IReadOnlyList<MetricCategory> Categories { get; } =
    [
        new("cash", "Cash"),
        new("transports", "Transports"),
        new("payer", "Payers"),
        new("claims", "Claims and AR"),
        new("fleet", "Fleet and bases"),
        new("insights", "Insights"),
    ];

    public static IReadOnlyList<MetricCatalogEntry> Entries { get; } = Build();

    /// <summary>The entry for a Metric key, or null when the catalog has none.</summary>
    public static MetricCatalogEntry? Find(string? key) =>
        key is null ? null : Entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

    /// <summary>The months-shown choice, offered for every way the Metric can be drawn.</summary>
    private static WidgetOption MonthsShown(IReadOnlyList<WidgetPresentation> presentations) =>
        new("months", "Months shown", "choice", MonthChoices, "6", [.. presentations.Select(p => p.Key)]);

    private static WidgetPresentation Line(bool isDefault) => new("line", "Line", "show_chart", MediumUp, 6, 3, 12, 6, isDefault);

    private static WidgetPresentation Column(bool isDefault) => new("column", "Column", "bar_chart", MediumUp, 6, 3, 12, 6, isDefault);

    private static WidgetPresentation Stacked() => new("stacked", "Stacked column", "stacked_bar_chart", MediumUp, 6, 3, 12, 6, false);

    private static WidgetPresentation Donut() => new("donut", "Donut", "donut_large", MediumUp, 3, 3, 6, 6, false);

    private static MetricCatalogEntry Available(
        MetricDefinition definition,
        string category,
        IReadOnlyList<string> serviceLines,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<WidgetPresentation> presentations,
        MetricAppliesTo appliesTo = MetricAppliesTo.Both) =>
        new(
            definition.Key,
            definition.Name,
            definition.Description,
            definition.Unit,
            category,
            MetricStatus.Available,
            true,
            false,
            serviceLines,
            dimensions,
            presentations,
            dimensions.Contains(MetricDimensions.Months) ? [MonthsShown(presentations)] : [])
        {
            Context = new MetricContext(
                true,
                serviceLines,
                dimensions.Contains(MetricDimensions.Division) ? DivisionSupport.Supported : DivisionSupport.None,
                dimensions.Contains(MetricDimensions.Group) ? [MetricDimensions.Group] : [],
                dimensions.Contains(MetricDimensions.Months) ? "month" : "period",
                appliesTo),
        };

    private static MetricCatalogEntry Planned(string key, string name, string description, string unit, string category, bool claimLevel = false) =>
        new(key, name, description, unit, category, MetricStatus.Planned, false, claimLevel, [], [], [], []);

    private static List<MetricCatalogEntry> Build() =>
    [
        Available(INetCollectionsMetric.Definition, "cash", AllLines, SeriesAndDivision, [Line(true), Column(false), Table]),
        Available(INetCashProjectionMetric.Definition, "cash", AllLines, PeriodAndLine, [Column(true), Line(false), Table]),
        Available(INetCashBreakdownMetric.Definition, "cash", AllLines, PeriodLineAndDivision, [Table with { IsDefault = true }, Column(false)])
            with { Drills = [new MetricDrill("drill", "payer_mix", MetricDimensions.Group)] },
        Available(INetCashByGroupMetric.Definition, "cash", AllLines, PeriodLineAndDivision, [Stacked(), Column(true), Table]),
        Available(ICashPerTransportMetric.Definition, "cash", AllLines, PeriodAndDivision, [Column(true), Table]),
        Available(IPaymentAgeMetric.Definition, "payer", AllLines, PeriodGroupAndDivision, [Table with { IsDefault = true }]),
        Available(IPayerMixMetric.Definition, "payer", AllLines, PeriodGroupAndDivision, [Column(true), Stacked(), Donut(), Table])
            with { Drills = [new MetricDrill("drill", "payer_mix_detail", MetricDimensions.PayerGroup)] },
        Available(ITransportVolumeMetric.Definition, "transports", AllLines, SeriesAndDivision, [Tile with { IsDefault = true }, Column(false), Table]),
        Available(ICleanClaimRateMetric.Definition, "claims", AllLines, PeriodAndMonths, [Tile with { IsDefault = true }, Line(false), Table]),

        Available(ITransportMixByCallTypeMetric.Definition, "transports", AllLines, TrendGroupAndDivision, [Column(true), Stacked(), Donut(), Line(false), Table]),

        Available(IAircraftPerformanceMetric.Definition, "fleet", ServiceLineScope.LinesFor(MetricAppliesTo.Air), PeriodGroupAndDivision, [Table with { IsDefault = true }, Column(false)], MetricAppliesTo.Air),

        Available(IBaseAnalysisMetric.Definition, "fleet", AllLines, PeriodGroupAndDivision, [Table with { IsDefault = true }, Column(false)]),

        Available(ILoadedMilesByAircraftMetric.Definition, "fleet", ServiceLineScope.LinesFor(MetricAppliesTo.Air), TrendGroupAndDivision, [Column(true), Line(false), Table], MetricAppliesTo.Air),

        Available(IPayerMixFinancialEffectMetric.Definition, "payer", AllLines, PeriodGroupAndDivision, [Column(true), Tile with { IsDefault = false }, Table]),

        Available(ICashContributionByAircraftMetric.Definition, "cash", ServiceLineScope.LinesFor(MetricAppliesTo.Air), PeriodGroupAndDivision, [Table with { IsDefault = true }, Column(false)], MetricAppliesTo.Air),
        Available(ICashContributionByDivisionMetric.Definition, "cash", AllLines, PeriodGroupAndDivision, [Table with { IsDefault = true }, Column(false)]),
        Available(ICashContributionByPayerMetric.Definition, "cash", AllLines, PeriodGroupAndDivision, [Table with { IsDefault = true }, Column(false)])
            with { Drills = [new MetricDrill("drill", "payer_mix_detail", MetricDimensions.PayerGroup)] },

        Available(ITransportsByPickupLocationMetric.Definition, "transports", AllLines, PeriodGroupAndDivision, [Column(true), Donut(), Table]),

        // A drill target only: reached from a payer group on payer mix, never placed on a Saved View, so it has no presentations of its own.
        Available(IPayerMixDetailMetric.Definition, "payer", AllLines, PayerMixDetailDimensions, []) with { Placeable = false },

        // Computed on the dashboard from other Metrics. Not placeable on a Saved View.
        new MetricCatalogEntry("what_changed", "What changed", "A written summary of the findings behind a change in net cash. Shown on the dashboard only.", "text", "insights", MetricStatus.Available, false, false, AllLines, PeriodLineAndDivision, [], [])
            with { Context = new MetricContext(true, AllLines, DivisionSupport.Supported, [], "period") },

        Planned("net_cash_by_aircraft_type", "Net cash by aircraft type", "Rotor wing and fixed wing cash, with the six-month trend.", "USD", "fleet"),
        Planned("expected_cash_by_payer", "Expected cash by payer", "Projected cash by payer class, spread over the months payers are expected to pay.", "USD", "payer"),
        Planned("payment_timing_by_payer", "Payment timing by payer", "How long each payer class usually takes to pay.", "days", "payer"),
        Planned("ar_days", "AR days", "Days in accounts receivable, with the trend.", "days", "claims"),
        Planned("open_negotiations", "Open negotiations", "No Surprises Act negotiations that are open, close to expiring, or settled.", "negotiations", "claims"),
        Planned("claim_status_distribution", "Claim status distribution", "Claims by status: denied, IDR, patient due, in review, pending and paid.", "claims", "claims"),
        Planned("dollars_at_risk", "Dollars at risk", "Expected payment tied up in denied, negotiated and patient-due claims.", "USD", "claims"),
        Planned("denied_claims", "Denied claims", "Denied claims with payer, reason and deadline.", "claims", "claims", claimLevel: true),
        Planned("claims_needing_attention", "Claims needing attention", "Claims in denied, IDR, patient-due and other exception statuses.", "claims", "claims", claimLevel: true),
        Planned("timely_filing_risk", "Timely filing risk", "Claims closest to their filing deadline.", "claims", "claims", claimLevel: true),
        Planned("expected_payment_per_transport", "Expected payment per transport", "Expected payment for each transport after contractual adjustments, against the Provider average.", "USD per transport", "cash"),
    ];
}
