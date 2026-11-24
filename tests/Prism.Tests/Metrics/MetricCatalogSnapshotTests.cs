using System.Globalization;
using System.Text;
using System.Text.Json;
using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

/// <summary>
/// The Metric Catalog is the source of truth. The web fixture (<c>web/src/app/metrics/catalog.fixture.json</c>) and the generated table in
/// <c>docs/metrics.md</c> are derived from it and committed, and either one going stale fails here. To accept a change:
/// <c>PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests --filter MetricCatalogSnapshotTests</c>, then review the diff.
/// </summary>
public sealed class MetricCatalogSnapshotTests
{
    private const string StartMarker = "<!-- generated:start -->";
    private const string EndMarker = "<!-- generated:end -->";
    private const string FixtureComment = "Generated from src/Prism.Application/Metrics/MetricCatalog.cs by MetricCatalogSnapshotTests. Do not edit by hand: run the test with PRISM_UPDATE_SNAPSHOTS=1. Lists every Available Metric the web registries must know: placeable ones and drill targets.";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    // Backend slice folders (relative to Application/Metrics) whose name is not the PascalCase of the key. Every other Metric uses PascalCase(key); the test checks the folder exists.
    private static readonly Dictionary<string, string> SliceFolderExceptions = new(StringComparer.Ordinal)
    {
        ["cash_per_transport_by_payer"] = "CashPerTransport",
        ["what_changed"] = "../Insights",
    };

    private static bool Update => Environment.GetEnvironmentVariable("PRISM_UPDATE_SNAPSHOTS") == "1";

    private static IEnumerable<MetricCatalogEntry> Available => MetricCatalog.Entries.Where(e => e.Status == MetricStatus.Available);

    [Fact]
    public void The_web_catalog_fixture_matches_the_catalog()
    {
        string path = Path.Combine(FindRoot(), "web", "src", "app", "metrics", "catalog.fixture.json");
        string current = JsonSerializer.Serialize(
            new
            {
                _comment = FixtureComment,
                metrics = Available.Where(e => e.CanBePlaced || IsDrillTarget(e)).Select(e => new
                {
                    key = e.Key,
                    status = "available",
                    placeable = e.CanBePlaced,
                    presentations = e.Presentations.Select(p => p.Key),
                    serviceLines = e.ServiceLines,
                    appliesTo = Lower(e.Context.AppliesTo.ToString()),
                    divisionSupport = Lower(e.Context.DivisionSupport.ToString()),
                    freshness = Lower(e.Freshness.Policy.ToString()),
                    helpKey = e.HelpKey,
                    drills = e.Drills.Select(d => new { rel = d.Rel, targetKey = d.TargetKey, bindsDimension = d.BindsDimension }),
                }),
            },
            Pretty) + "\n";

        AssertCurrent(path, current);
    }

    [Fact]
    public void The_generated_table_in_the_metrics_doc_matches_the_catalog()
    {
        string path = Path.Combine(FindRoot(), "docs", "metrics.md");
        string doc = File.ReadAllText(path).ReplaceLineEndings("\n");
        int start = doc.IndexOf(StartMarker, StringComparison.Ordinal);
        int end = doc.IndexOf(EndMarker, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"docs/metrics.md needs {StartMarker} and {EndMarker} markers, in that order.");

        string current = doc[..(start + StartMarker.Length)] + "\n" + Table() + doc[end..];

        AssertCurrent(path, current);
    }

    private static string Table()
    {
        var table = new StringBuilder();
        table.Append("| Key | Category | Unit | Applies to | Service lines | Freshness | Division | Drills | Placeable | Renderer folder (`web/src/app/metrics/`) | Backend folder (`Metrics/` in Application, Infrastructure, Api) |\n");
        table.Append("|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (MetricCatalogEntry e in Available)
        {
            string drills = e.Drills.Count == 0 ? "none" : string.Join(", ", e.Drills.Select(d => $"{d.Rel} to `{d.TargetKey}` (binds `{d.BindsDimension}`)"));
            string category = MetricCatalog.Categories.FirstOrDefault(c => c.Key == e.Category)?.Name ?? e.Category;
            table.Append(CultureInfo.InvariantCulture, $"| `{e.Key}` | {category} | {e.Unit} | {Lower(e.Context.AppliesTo.ToString())} | {string.Join(", ", e.ServiceLines)} | {e.Freshness.Policy} | {e.Context.DivisionSupport} | {drills} | {(e.CanBePlaced ? "yes" : "no")} | {RendererFolder(e.Key)} | `{SliceFolder(e.Key)}` |\n");
        }

        return table.ToString();
    }

    private static bool IsDrillTarget(MetricCatalogEntry entry) => MetricCatalog.Entries.Any(e => e.Drills.Any(d => d.TargetKey == entry.Key));

    private static string RendererFolder(string key)
    {
        string kebab = key.Replace('_', '-');
        return Directory.Exists(Path.Combine(FindRoot(), "web", "src", "app", "metrics", kebab)) ? $"`{kebab}/`" : "none";
    }

    private static string SliceFolder(string key)
    {
        string folder = SliceFolderExceptions.TryGetValue(key, out string? mapped)
            ? mapped
            : string.Concat(key.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        string dir = Path.Combine(FindRoot(), "src", "Prism.Application", "Metrics", folder);
        Assert.True(Directory.Exists(dir), $"No backend slice folder {folder} for Metric {key}. Create it, or add it to SliceFolderExceptions.");
        return folder.Replace("../", string.Empty, StringComparison.Ordinal);
    }

    private static void AssertCurrent(string path, string current)
    {
        if (Update)
        {
            File.WriteAllText(path, current);
        }

        Assert.True(File.Exists(path), $"{path} does not exist. Create it by running with PRISM_UPDATE_SNAPSHOTS=1.");
        string committed = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(committed == current, $"{Path.GetFileName(path)} is stale against the Metric Catalog. Run with PRISM_UPDATE_SNAPSHOTS=1 (dotnet test tests/Prism.Tests --filter MetricCatalogSnapshotTests) and review the diff.");
    }

    private static string Lower(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static string FindRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "global.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("global.json not found above the test binaries.");
    }
}
