namespace Prism.Application.Personas;

/// <summary>Bounds on what staff can configure and what a user can submit.</summary>
public static class PersonaLimits
{
    public const int MaxPersonas = 20;
    public const int MaxQuestions = 10;

    /// <summary>The questionnaire is two to three questions. A fourth cannot be published.</summary>
    public const int MinPublishedQuestions = 2;
    public const int MaxPublishedQuestions = 3;

    public const int MinOptions = 2;
    public const int MaxOptions = 8;
    public const int MaxWeight = 100;
    public const int MaxNameLength = 80;
    public const int MaxPromptLength = 200;
    public const int MaxLabelLength = 120;
    public const int MaxSortOrder = 1000;

    /// <summary>A submission is one answer for a single-choice question and one per chosen option for a multiple-choice one, so it can never be longer than this.</summary>
    public const int MaxAnswers = MaxPublishedQuestions * MaxOptions;

    public const int MaxRequestBodyBytes = 16 * 1024;
}
