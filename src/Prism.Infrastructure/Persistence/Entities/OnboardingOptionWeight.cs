namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>How much choosing an option counts toward a Persona.</summary>
public sealed class OnboardingOptionWeight
{
    public int Id { get; set; }

    public int OnboardingOptionId { get; set; }

    public int PersonaId { get; set; }

    public int Weight { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
