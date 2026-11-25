using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prism.Application.Personas;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Personas;

/// <summary>
/// The first-login questionnaire. A questionnaire is usable only when two or three questions are published, each with at least two options, and at
/// least one published Persona points at a published View Template. Anything less, or any failure to read it, means nobody is made to answer.
/// The answers are scored and thrown away: only the Persona and the completion time are stored.
/// </summary>
public sealed partial class OnboardingService(PrismDbContext db, TimeProvider clock, ILogger<OnboardingService> logger) : IOnboardingService
{
    public async Task<bool> IsRequiredAsync(long portalUserId, CancellationToken cancellationToken)
    {
        try
        {
            if (await db.UserProfiles.AsNoTracking().AnyAsync(p => p.PortalUserId == portalUserId && p.PersonaId != null, cancellationToken))
            {
                return false;
            }

            return await LoadAsync(cancellationToken) is not null;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            LogLoadFailed(ex.GetType().Name);
            return false;
        }
    }

    public async Task<IReadOnlyList<OnboardingQuestionView>?> GetQuestionnaireAsync(CancellationToken cancellationToken)
    {
        try
        {
            Questionnaire? questionnaire = await LoadAsync(cancellationToken);
            return questionnaire?.Questions.Select(q => new OnboardingQuestionView(q.Id, q.Prompt, q.AllowsMultiple, [.. q.Options.Select(o => new OnboardingOptionView(o.Id, o.Label))])).ToArray();
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            LogLoadFailed(ex.GetType().Name);
            return null;
        }
    }

    public async Task<OnboardingSubmitStatus> SubmitAsync(long portalUserId, IReadOnlyList<OnboardingAnswer> answers, CancellationToken cancellationToken)
    {
        Questionnaire? questionnaire = await LoadAsync(cancellationToken);
        if (questionnaire is null)
        {
            return OnboardingSubmitStatus.NotActive;
        }

        if (Score(questionnaire, answers) is not { } personaId)
        {
            return OnboardingSubmitStatus.Invalid;
        }

        UserProfile? profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.PortalUserId == portalUserId, cancellationToken);
        if (profile is null)
        {
            profile = new UserProfile { PortalUserId = portalUserId };
            db.UserProfiles.Add(profile);
        }

        profile.PersonaId = personaId;
        profile.OnboardingCompletedAtTimestamp = clock.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // A second tab saved first. The profile exists now, so update it.
            db.ChangeTracker.Clear();
            await db.UserProfiles.Where(p => p.PortalUserId == portalUserId).ExecuteUpdateAsync(
                set => set.SetProperty(p => p.PersonaId, personaId).SetProperty(p => p.OnboardingCompletedAtTimestamp, clock.GetUtcNow()),
                cancellationToken);
        }

        return OnboardingSubmitStatus.Saved;
    }

    /// <summary>
    /// The Persona the answers select, or null when they are not valid: every question needs an answer, a single-choice question exactly one, an option cannot be chosen twice,
    /// and every option has to belong to its question. The weights of every chosen option are added together.
    /// </summary>
    internal static int? Score(Questionnaire questionnaire, IReadOnlyList<OnboardingAnswer> answers)
    {
        if (answers.Count == 0 || answers.Count > PersonaLimits.MaxAnswers || answers.Select(a => (a.QuestionId, a.OptionId)).Distinct().Count() != answers.Count)
        {
            return null;
        }

        var totals = new Dictionary<int, int>();
        foreach (QuestionData question in questionnaire.Questions)
        {
            OnboardingAnswer[] chosen = [.. answers.Where(a => a.QuestionId == question.Id)];
            if (chosen.Length == 0 || (chosen.Length > 1 && !question.AllowsMultiple))
            {
                return null;
            }

            foreach (OnboardingAnswer answer in chosen)
            {
                if (question.Options.FirstOrDefault(o => o.Id == answer.OptionId) is not { } option)
                {
                    return null;
                }

                foreach ((int personaId, int weight) in option.Weights)
                {
                    totals[personaId] = totals.GetValueOrDefault(personaId) + weight;
                }
            }
        }

        // An answer for a question that is not in the questionnaire is not a valid submission.
        return answers.All(a => questionnaire.Questions.Any(q => q.Id == a.QuestionId)) ? PersonaScoring.Pick(questionnaire.Personas, totals) : null;
    }

    private async Task<Questionnaire?> LoadAsync(CancellationToken cancellationToken)
    {
        List<OnboardingQuestion> questions = await db.OnboardingQuestions.AsNoTracking()
            .Where(q => q.IsPublished)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Take(PersonaLimits.MaxPublishedQuestions + 1)
            .ToListAsync(cancellationToken);
        if (questions.Count is < PersonaLimits.MinPublishedQuestions or > PersonaLimits.MaxPublishedQuestions)
        {
            return null;
        }

        // A Persona counts only while it and its template are both published.
        List<PersonaCandidate> personas = (await db.Personas.AsNoTracking()
            .Where(p => p.IsPublished && db.ViewTemplates.Any(t => t.Id == p.ViewTemplateId && t.IsPublished))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Take(PersonaLimits.MaxPersonas)
            .Select(p => new PersonaCandidate(p.Id, p.SortOrder))
            .ToListAsync(cancellationToken));
        if (personas.Count == 0)
        {
            return null;
        }

        int[] questionIds = [.. questions.Select(q => q.Id)];
        List<OnboardingOption> options = await db.OnboardingOptions.AsNoTracking()
            .Where(o => questionIds.Contains(o.OnboardingQuestionId))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);
        int[] optionIds = [.. options.Select(o => o.Id)];
        HashSet<int> usable = [.. personas.Select(p => p.Id)];
        ILookup<int, (int PersonaId, int Weight)> weights = (await db.OnboardingOptionWeights.AsNoTracking()
            .Where(w => optionIds.Contains(w.OnboardingOptionId))
            .ToListAsync(cancellationToken))
            .Where(w => usable.Contains(w.PersonaId))
            .ToLookup(w => w.OnboardingOptionId, w => (w.PersonaId, w.Weight));
        ILookup<int, OnboardingOption> byQuestion = options.ToLookup(o => o.OnboardingQuestionId);

        QuestionData[] data = [.. questions.Select(q => new QuestionData(
            q.Id,
            q.Prompt,
            q.AllowsMultiple,
            [.. byQuestion[q.Id].Select(o => new OptionData(o.Id, o.Label, [.. weights[o.Id]]))]))];
        return data.Any(q => q.Options.Count < PersonaLimits.MinOptions) ? null : new Questionnaire(data, personas);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The onboarding questionnaire could not be read ({ExceptionType}), so the user is not asked")]
    private partial void LogLoadFailed(string exceptionType);

    internal sealed record Questionnaire(IReadOnlyList<QuestionData> Questions, IReadOnlyList<PersonaCandidate> Personas);

    internal sealed record QuestionData(int Id, string Prompt, bool AllowsMultiple, IReadOnlyList<OptionData> Options);

    internal sealed record OptionData(int Id, string Label, IReadOnlyList<(int PersonaId, int Weight)> Weights);
}
