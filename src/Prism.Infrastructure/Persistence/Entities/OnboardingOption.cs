namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>One answer a user can pick for a question.</summary>
public sealed class OnboardingOption
{
    public int Id { get; set; }

    public int OnboardingQuestionId { get; set; }

    public string Label { get; set; } = "";

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
