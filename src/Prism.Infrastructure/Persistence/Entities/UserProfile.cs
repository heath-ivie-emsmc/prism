namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>
/// What Prism remembers about one signed-in user, across Providers: their Persona and when they finished the questionnaire. The raw answers are never stored.
/// </summary>
public sealed class UserProfile
{
    public int Id { get; set; }

    public long PortalUserId { get; set; }

    public int? PersonaId { get; set; }

    public DateTimeOffset? OnboardingCompletedAtTimestamp { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
