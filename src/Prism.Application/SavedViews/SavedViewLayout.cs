using Prism.Application.Metrics;

namespace Prism.Application.SavedViews;

/// <summary>The bounds on everything a client can send about a Saved View. Each one is enforced on the server, whatever the browser does.</summary>
public static class SavedViewLimits
{
    public const int Columns = 12;
    public const int MaxRows = 200;
    public const int MaxWidgets = 24;
    public const int MaxLayoutBytes = 32 * 1024;
    public const int MaxRequestBodyBytes = 64 * 1024;
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 300;
    public const int MaxWidgetIdLength = 36;
    public const int MaxOptionsPerWidget = 8;
    public const int MaxOptionTextLength = 32;
    public const int MaxEmailLength = 320;
    public const int MaxEmailsPerRequest = 10;
    public const int MaxRecipientsPerView = 50;
    public const int MaxViewsPerOwner = 100;
    public const int MaxListedViews = 200;
    public const int MaxInvitationsPerOwnerPerHour = 30;
    public const int CurrentLayoutVersion = 1;
}

/// <summary>One Metric on the grid. It names a Metric by key and says how to draw it and where. It never carries a value, a Label or a filter.</summary>
public sealed record LayoutWidget(
    string Id,
    string MetricKey,
    string Presentation,
    IReadOnlyDictionary<string, string>? Options,
    int X,
    int Y,
    int W,
    int H);

/// <summary>The saved arrangement: a version, a density (<c>comfortable</c> or <c>compact</c>) and the widgets on a 12-column grid.</summary>
public sealed record SavedViewLayout(int Version, string? Density, IReadOnlyList<LayoutWidget>? Widgets);

/// <summary>
/// Checks a layout against the Metric Catalog and the grid. It returns codes, never the offending value, so an error can be shown or logged
/// without echoing what the client sent.
/// </summary>
public static class LayoutValidator
{
    public const string BadVersion = "bad_version";
    public const string BadDensity = "bad_density";
    public const string TooManyWidgets = "too_many_widgets";
    public const string BadWidgetId = "bad_widget_id";
    public const string DuplicateWidgetId = "duplicate_widget_id";
    public const string UnknownMetric = "unknown_metric";
    public const string MetricNotPlaceable = "metric_not_placeable";
    public const string PresentationNotAllowed = "presentation_not_allowed";
    public const string OptionNotAllowed = "option_not_allowed";
    public const string OptionValueInvalid = "option_value_invalid";
    public const string OutsideGrid = "outside_grid";
    public const string SizeOutOfBounds = "size_out_of_bounds";
    public const string Overlap = "overlap";

    public static IReadOnlyList<string> Validate(SavedViewLayout? layout)
    {
        if (layout is null)
        {
            return [BadVersion];
        }

        List<string> problems = [];
        if (layout.Version != SavedViewLimits.CurrentLayoutVersion)
        {
            problems.Add(BadVersion);
        }

        if (layout.Density is not (null or "comfortable" or "compact"))
        {
            problems.Add(BadDensity);
        }

        IReadOnlyList<LayoutWidget> widgets = layout.Widgets ?? [];
        if (widgets.Count > SavedViewLimits.MaxWidgets)
        {
            // Do not look at any of them: a flood of widgets is not worth the work of finding every fault.
            problems.Add(TooManyWidgets);
            return problems;
        }

        HashSet<string> ids = [];
        foreach (LayoutWidget widget in widgets)
        {
            CheckWidget(widget, ids, problems);
        }

        if (!problems.Contains(OutsideGrid, StringComparer.Ordinal) && HasOverlap(widgets))
        {
            problems.Add(Overlap);
        }

        return [.. problems.Distinct(StringComparer.Ordinal)];
    }

    private static void CheckWidget(LayoutWidget widget, HashSet<string> ids, List<string> problems)
    {
        if (!IsValidId(widget.Id))
        {
            problems.Add(BadWidgetId);
        }
        else if (!ids.Add(widget.Id))
        {
            problems.Add(DuplicateWidgetId);
        }

        MetricCatalogEntry? entry = MetricCatalog.Find(widget.MetricKey);
        if (entry is null)
        {
            problems.Add(UnknownMetric);
            return;
        }

        if (!entry.CanBePlaced)
        {
            problems.Add(MetricNotPlaceable);
            return;
        }

        WidgetPresentation? presentation = entry.Presentations.FirstOrDefault(p => string.Equals(p.Key, widget.Presentation, StringComparison.Ordinal));
        if (presentation is null)
        {
            problems.Add(PresentationNotAllowed);
            return;
        }

        CheckGeometry(widget, presentation, problems);
        CheckOptions(widget, entry, problems);
    }

    private static void CheckGeometry(LayoutWidget widget, WidgetPresentation presentation, List<string> problems)
    {
        if (widget.W < presentation.MinWidth || widget.W > presentation.MaxWidth || widget.H < presentation.MinHeight || widget.H > presentation.MaxHeight)
        {
            problems.Add(SizeOutOfBounds);
        }

        if (widget.X < 0 || widget.Y < 0 || widget.W < 1 || widget.H < 1
            || (long)widget.X + widget.W > SavedViewLimits.Columns || (long)widget.Y + widget.H > SavedViewLimits.MaxRows)
        {
            problems.Add(OutsideGrid);
        }
    }

    private static void CheckOptions(LayoutWidget widget, MetricCatalogEntry entry, List<string> problems)
    {
        if (widget.Options is not { Count: > 0 } options)
        {
            return;
        }

        if (options.Count > SavedViewLimits.MaxOptionsPerWidget)
        {
            problems.Add(OptionNotAllowed);
            return;
        }

        foreach ((string key, string value) in options)
        {
            WidgetOption? option = entry.Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
            if (option is null || !option.AppliesTo.Contains(widget.Presentation, StringComparer.Ordinal))
            {
                problems.Add(OptionNotAllowed);
                continue;
            }

            bool valid = option.Type switch
            {
                "choice" => option.Choices.Contains(value, StringComparer.Ordinal),
                "toggle" => value is "true" or "false",
                _ => false,
            };
            if (!valid || value.Length > SavedViewLimits.MaxOptionTextLength)
            {
                problems.Add(OptionValueInvalid);
            }
        }
    }

    // Letters, digits and hyphens only: an id is a handle for the browser, never text.
    private static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= SavedViewLimits.MaxWidgetIdLength && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static bool HasOverlap(IReadOnlyList<LayoutWidget> widgets)
    {
        for (int i = 0; i < widgets.Count; i++)
        {
            for (int j = i + 1; j < widgets.Count; j++)
            {
                LayoutWidget a = widgets[i];
                LayoutWidget b = widgets[j];
                if (a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
