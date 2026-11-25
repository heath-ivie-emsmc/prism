namespace Prism.Infrastructure.Persistence.Entities;

/// <summary>One question of the first-login questionnaire. Only published questions are shown.</summary>
public sealed class OnboardingQuestion
{
    public int Id { get; set; }

    public string Prompt { get; set; } = "";

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>True when a user may choose more than one option (choose all that apply). Their weights are added together.</summary>
    public bool AllowsMultiple { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset CreatedAtTimestamp { get; set; }

    public DateTimeOffset UpdatedAtTimestamp { get; set; }
}
