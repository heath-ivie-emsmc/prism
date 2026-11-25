namespace Prism.Application.FeatureManagement;

/// <summary>Who a flag is being asked about. A Provider id or a portal user id, never a name or anything from a request body.</summary>
public sealed record FeatureContext
{
    public static FeatureContext Empty { get; } = new();

    public long? ProviderId { get; init; }

    public long? UserId { get; init; }
}
