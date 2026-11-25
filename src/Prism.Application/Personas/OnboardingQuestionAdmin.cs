using Prism.Application.Admin;

namespace Prism.Application.Personas;

public sealed record WeightContent(int PersonaId, int Weight);

public sealed record OptionContent(string Label, int SortOrder, IReadOnlyList<WeightContent> Weights);

public sealed record QuestionContent(string Prompt, int SortOrder, bool AllowsMultiple, IReadOnlyList<OptionContent> Options);

public sealed record OptionDetail(int Id, string Label, int SortOrder, IReadOnlyList<WeightContent> Weights);

public sealed record QuestionDetail(int Id, string Prompt, int SortOrder, bool IsPublished, bool AllowsMultiple, IReadOnlyList<OptionDetail> Options);

public enum QuestionWriteStatus
{
    Saved,
    NotFound,
    LimitReached,

    /// <summary>A weight names a Persona that does not exist.</summary>
    UnknownPersona,

    /// <summary>A fourth question cannot be published: the questionnaire is two to three.</summary>
    TooManyPublished,
}

public sealed record QuestionWriteResult(QuestionWriteStatus Status, QuestionDetail? Question = null);

/// <summary>Questionnaire questions, for Internal Users. Only published questions are ever shown to a user.</summary>
public interface IOnboardingQuestionStore
{
    Task<IReadOnlyList<QuestionDetail>> ListAsync(CancellationToken cancellationToken);

    Task<QuestionDetail?> GetAsync(int questionId, CancellationToken cancellationToken);

    Task<QuestionWriteResult> CreateAsync(AdminActor actor, QuestionContent content, CancellationToken cancellationToken);

    /// <summary>Replaces the question's options and weights. No answer is stored anywhere, so nothing refers to an option.</summary>
    Task<QuestionWriteResult> UpdateAsync(AdminActor actor, int questionId, QuestionContent content, CancellationToken cancellationToken);

    Task<QuestionWriteResult> SetPublishedAsync(AdminActor actor, int questionId, bool published, CancellationToken cancellationToken);

    Task<QuestionWriteResult> DeleteAsync(AdminActor actor, int questionId, CancellationToken cancellationToken);
}
