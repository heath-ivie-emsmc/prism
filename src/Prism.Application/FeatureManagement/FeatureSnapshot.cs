namespace Prism.Application.FeatureManagement;

public sealed record FeatureSnapshot
{
    public required DateTimeOffset RetrievedAt { get; init; }

    public required IReadOnlyDictionary<string, FeatureValue> Features { get; init; }
}
