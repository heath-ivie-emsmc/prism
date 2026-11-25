using System.Collections.Immutable;


using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class SidecarState
{
    private Snapshot _snapshot = Snapshot.Empty;

    public Snapshot GetSnapshot() => Volatile.Read(ref _snapshot);

    public bool TryGetFeature(string featureName, out FeatureValue? featureValue)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            featureValue = null;
            return false;
        }

        return Volatile.Read(ref _snapshot).Features.TryGetValue(featureName, out featureValue);
    }

    public void Replace(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _snapshot, snapshot);
    }

    public sealed record Snapshot(DateTimeOffset RetrievedAt, IImmutableDictionary<string, FeatureValue> Features)
    {
        public static Snapshot Empty { get; } = new(
            DateTimeOffset.MinValue,
            ImmutableDictionary.Create<string, FeatureValue>(StringComparer.OrdinalIgnoreCase));
    }
}
