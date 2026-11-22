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
}

/// <summary>The filter names a Metric can list.</summary>
public static class MetricDimensions
{
    public const string Period = "period";
    public const string Months = "months";
    public const string ServiceLine = "serviceLine";
    public const string Group = "group";
    public const string Division = "division";
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
    private static readonly string[] SeriesDimensions = [MetricDimensions.Period, MetricDimensions.Months, MetricDimensions.ServiceLine];
    private static readonly string[] PeriodAndLine = [MetricDimensions.Period, MetricDimensions.ServiceLine];
    private static readonly string[] PeriodAndGroup = [MetricDimensions.Period, MetricDimensions.ServiceLine, MetricDimensions.Group];
    private static readonly string[] PeriodOnly = [MetricDimensions.Period];
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
        IReadOnlyList<WidgetPresentation> presentations) =>
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
            dimensions.Contains(MetricDimensions.Months) ? [MonthsShown(presentations)] : []);

    private static MetricCatalogEntry Planned(string key, string name, string description, string unit, string category, bool claimLevel = false) =>
        new(key, name, description, unit, category, MetricStatus.Planned, false, claimLevel, [], [], [], []);

    private static List<MetricCatalogEntry> Build() =>
    [
        Available(INetCollectionsMetric.Definition, "cash", AllLines, SeriesDimensions, [Line(true), Column(false), Table]),
        Available(INetCashProjectionMetric.Definition, "cash", AllLines, PeriodAndLine, [Column(true), Line(false), Table]),
        Available(INetCashBreakdownMetric.Definition, "cash", AllLines, PeriodAndLine, [Table with { IsDefault = true }, Column(false)]),
        Available(INetCashByGroupMetric.Definition, "cash", AllLines, PeriodAndLine, [Stacked(), Column(true), Table]),
        Available(ICashPerTransportMetric.Definition, "cash", AllLines, PeriodOnly, [Column(true), Table]),
        Available(IPaymentAgeMetric.Definition, "payer", AllLines, PeriodAndGroup, [Table with { IsDefault = true }]),
        Available(IPayerMixMetric.Definition, "payer", AllLines, PeriodAndGroup, [Column(true), Stacked(), Donut(), Table]),
        Available(ITransportVolumeMetric.Definition, "transports", AllLines, SeriesDimensions, [Tile with { IsDefault = true }, Column(false), Table]),
        Available(ICleanClaimRateMetric.Definition, "claims", AllLines, PeriodAndMonths, [Tile with { IsDefault = true }, Line(false), Table]),

        // Computed on the dashboard from other Metrics. Not placeable on a Saved View.
        new("what_changed", "What changed", "A written summary of the findings behind a change in net cash. Shown on the dashboard only.", "text", "insights", MetricStatus.Available, false, false, AllLines, PeriodAndLine, [], []),

        Planned("net_cash_by_aircraft_type", "Net cash by aircraft type", "Rotor wing and fixed wing cash, with the six-month trend.", "USD", "fleet"),
        Planned("aircraft_performance", "Aircraft performance", "Transports, loaded miles and net cash by tail number, with change from the prior month.", "USD", "fleet"),
        Planned("base_analysis", "Base analysis", "Net cash and transport volume by base, and the aircraft operating from each.", "USD", "fleet"),
        Planned("loaded_miles_by_aircraft", "Loaded miles by aircraft", "Loaded miles for each tail number.", "miles", "fleet"),
        Planned("transport_mix_by_call_type", "Scene and interfacility mix", "Transports by call type, split by aircraft type.", "transports", "transports"),
        Planned("transports_by_pickup_location", "Transports by pickup location", "Transport counts by sending facility and scene response area.", "transports", "transports"),
        Planned("expected_cash_by_payer", "Expected cash by payer", "Projected cash by payer class, spread over the months payers are expected to pay.", "USD", "payer"),
        Planned("payment_timing_by_payer", "Payment timing by payer", "How long each payer class usually takes to pay.", "days", "payer"),
        Planned("payer_mix_financial_effect", "Financial effect of payer mix", "The estimated cash effect of a shift in payer mix.", "USD", "payer"),
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
