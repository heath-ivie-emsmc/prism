namespace Prism.Application.Personas;

/// <summary>One question as a user sees it. The weights stay on the server. With <paramref name="AllowsMultiple"/> the user chooses all that apply, otherwise exactly one.</summary>
public sealed record OnboardingQuestionView(int Id, string Prompt, bool AllowsMultiple, IReadOnlyList<OnboardingOptionView> Options);

public sealed record OnboardingOptionView(int Id, string Label);

/// <summary>One chosen option for one question. A multiple-choice question has one answer for each option chosen.</summary>
public sealed record OnboardingAnswer(int QuestionId, int OptionId);

public enum OnboardingSubmitStatus
{
    Saved,

    /// <summary>No usable questionnaire is published, so there is nothing to submit to.</summary>
    NotActive,

    /// <summary>A question with no answer, more than one answer to a single-choice question, an option twice, or an option that is not in that question.</summary>
    Invalid,
}

/// <summary>
/// The first-login questionnaire for the signed-in user. It fails open: when it is not configured or cannot be read, nobody is made to answer it.
/// The user is always the session's, passed in by server code.
/// </summary>
public interface IOnboardingService
{
    /// <summary>True only when the user has no Persona and a usable questionnaire is published. False on any failure.</summary>
    Task<bool> IsRequiredAsync(long portalUserId, CancellationToken cancellationToken);

    /// <summary>The published questions in order, or null when the questionnaire is not usable.</summary>
    Task<IReadOnlyList<OnboardingQuestionView>?> GetQuestionnaireAsync(CancellationToken cancellationToken);

    /// <summary>Scores the answers and stores only the resulting Persona and the completion time.</summary>
    Task<OnboardingSubmitStatus> SubmitAsync(long portalUserId, IReadOnlyList<OnboardingAnswer> answers, CancellationToken cancellationToken);
}
