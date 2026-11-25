using System.Text.Json;

namespace Prism.Application.FeatureManagement;

public sealed record FeatureValue
{
    public required string FeatureName { get; init; }

    public required bool Enabled { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Settings { get; init; }

    public string? Variant { get; init; }
}
