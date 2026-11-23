using System.Reflection;

namespace Prism.Api.Metrics;

/// <summary>Finds the <see cref="IMetricModule"/> implementations in this assembly, ordered by <see cref="IMetricModule.RouteOrder"/> then type name.</summary>
public static class MetricModules
{
    private static readonly Lazy<IReadOnlyList<IMetricModule>> All = new(() => Discover());

    public static IReadOnlyList<IMetricModule> Discover(Assembly assembly) =>
    [
        .. assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMetricModule).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IMetricModule)Activator.CreateInstance(t)!)
            .OrderBy(m => m.RouteOrder),
    ];

    public static IReadOnlyList<IMetricModule> Discover() => Discover(typeof(IMetricModule).Assembly);

    internal static IReadOnlyList<IMetricModule> Current => All.Value;
}
