using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Application.Personas;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Personas;

/// <summary>Questionnaire questions in Prism's own database. Each change and its audit record are saved in one transaction.</summary>
public sealed class OnboardingQuestionStore(PrismDbContext db) : IOnboardingQuestionStore
{
    public async Task<IReadOnlyList<QuestionDetail>> ListAsync(CancellationToken cancellationToken)
    {
        List<OnboardingQuestion> questions = await db.OnboardingQuestions.AsNoTracking()
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Take(PersonaLimits.MaxQuestions)
            .ToListAsync(cancellationToken);
        return await DescribeAsync(questions, cancellationToken);
    }

    public async Task<QuestionDetail?> GetAsync(int questionId, CancellationToken cancellationToken)
    {
        OnboardingQuestion? question = await db.OnboardingQuestions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        return question is null ? null : (await DescribeAsync([question], cancellationToken))[0];
    }

    public async Task<QuestionWriteResult> CreateAsync(AdminActor actor, QuestionContent content, CancellationToken cancellationToken)
    {
        if (await db.OnboardingQuestions.CountAsync(cancellationToken) >= PersonaLimits.MaxQuestions)
        {
            return new QuestionWriteResult(QuestionWriteStatus.LimitReached);
        }

        if (!await WeightsNamePersonasAsync(content, cancellationToken))
        {
            return new QuestionWriteResult(QuestionWriteStatus.UnknownPersona);
        }

        var question = new OnboardingQuestion { CreatedByUserName = actor.UserName, UpdatedByUserName = actor.UserName };
        question.Prompt = content.Prompt;
        question.SortOrder = content.SortOrder;
        question.AllowsMultiple = content.AllowsMultiple;
        db.OnboardingQuestions.Add(question);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await AddOptionsAsync(question.Id, content.Options, cancellationToken);
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.QuestionCreated, AdminAuditObjects.OnboardingQuestion, question.Id, null, content.Options.Count));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await SavedAsync(question.Id, cancellationToken);
    }

    public async Task<QuestionWriteResult> UpdateAsync(AdminActor actor, int questionId, QuestionContent content, CancellationToken cancellationToken)
    {
        OnboardingQuestion? question = await db.OnboardingQuestions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (question is null)
        {
            return new QuestionWriteResult(QuestionWriteStatus.NotFound);
        }

        if (!await WeightsNamePersonasAsync(content, cancellationToken))
        {
            return new QuestionWriteResult(QuestionWriteStatus.UnknownPersona);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        question.Prompt = content.Prompt;
        question.SortOrder = content.SortOrder;
        question.AllowsMultiple = content.AllowsMultiple;
        question.UpdatedByUserName = actor.UserName;
        await RemoveOptionsAsync(questionId, cancellationToken);
        await AddOptionsAsync(questionId, content.Options, cancellationToken);
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.QuestionUpdated, AdminAuditObjects.OnboardingQuestion, questionId, null, content.Options.Count));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await SavedAsync(questionId, cancellationToken);
    }

    public async Task<QuestionWriteResult> SetPublishedAsync(AdminActor actor, int questionId, bool published, CancellationToken cancellationToken)
    {
        OnboardingQuestion? question = await db.OnboardingQuestions.FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (question is null)
        {
            return new QuestionWriteResult(QuestionWriteStatus.NotFound);
        }

        if (published && !question.IsPublished
            && await db.OnboardingQuestions.CountAsync(q => q.IsPublished, cancellationToken) >= PersonaLimits.MaxPublishedQuestions)
        {
            return new QuestionWriteResult(QuestionWriteStatus.TooManyPublished);
        }

        question.IsPublished = published;
        question.UpdatedByUserName = actor.UserName;
        string action = published ? AdminAuditActions.QuestionPublished : AdminAuditActions.QuestionUnpublished;
        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(action, AdminAuditObjects.OnboardingQuestion, questionId));
        await db.SaveChangesAsync(cancellationToken);
        return await SavedAsync(questionId, cancellationToken);
    }

    public async Task<QuestionWriteResult> DeleteAsync(AdminActor actor, int questionId, CancellationToken cancellationToken)
    {
        if (!await db.OnboardingQuestions.AnyAsync(q => q.Id == questionId, cancellationToken))
        {
            return new QuestionWriteResult(QuestionWriteStatus.NotFound);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RemoveOptionsAsync(questionId, cancellationToken);
        int removed = await db.OnboardingQuestions.Where(q => q.Id == questionId).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return new QuestionWriteResult(QuestionWriteStatus.NotFound);
        }

        AdminStoreSupport.AddAudit(db, actor, new AdminAuditWrite(AdminAuditActions.QuestionDeleted, AdminAuditObjects.OnboardingQuestion, questionId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new QuestionWriteResult(QuestionWriteStatus.Saved);
    }

    private async Task<bool> WeightsNamePersonasAsync(QuestionContent content, CancellationToken cancellationToken)
    {
        int[] ids = [.. content.Options.SelectMany(o => o.Weights).Select(w => w.PersonaId).Distinct()];
        return ids.Length == 0 || await db.Personas.CountAsync(p => ids.Contains(p.Id), cancellationToken) == ids.Length;
    }

    private async Task RemoveOptionsAsync(int questionId, CancellationToken cancellationToken)
    {
        // Foreign keys restrict deletes, so the weights go before their options.
        await db.OnboardingOptionWeights
            .Where(w => db.OnboardingOptions.Any(o => o.Id == w.OnboardingOptionId && o.OnboardingQuestionId == questionId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.OnboardingOptions.Where(o => o.OnboardingQuestionId == questionId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task AddOptionsAsync(int questionId, IReadOnlyList<OptionContent> options, CancellationToken cancellationToken)
    {
        foreach (OptionContent content in options)
        {
            var option = new OnboardingOption { OnboardingQuestionId = questionId, Label = content.Label, SortOrder = content.SortOrder };
            db.OnboardingOptions.Add(option);
            await db.SaveChangesAsync(cancellationToken);
            db.OnboardingOptionWeights.AddRange(content.Weights.Select(w => new OnboardingOptionWeight
            {
                OnboardingOptionId = option.Id,
                PersonaId = w.PersonaId,
                Weight = w.Weight,
            }));
        }
    }

    private async Task<QuestionWriteResult> SavedAsync(int questionId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return new QuestionWriteResult(QuestionWriteStatus.Saved, await GetAsync(questionId, cancellationToken));
    }

    private async Task<IReadOnlyList<QuestionDetail>> DescribeAsync(List<OnboardingQuestion> questions, CancellationToken cancellationToken)
    {
        int[] ids = [.. questions.Select(q => q.Id)];
        List<OnboardingOption> options = await db.OnboardingOptions.AsNoTracking()
            .Where(o => ids.Contains(o.OnboardingQuestionId))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);
        int[] optionIds = [.. options.Select(o => o.Id)];
        ILookup<int, OnboardingOptionWeight> weights = (await db.OnboardingOptionWeights.AsNoTracking()
            .Where(w => optionIds.Contains(w.OnboardingOptionId))
            .OrderBy(w => w.PersonaId)
            .ToListAsync(cancellationToken)).ToLookup(w => w.OnboardingOptionId);
        ILookup<int, OnboardingOption> byQuestion = options.ToLookup(o => o.OnboardingQuestionId);

        return [.. questions.Select(q => new QuestionDetail(
            q.Id,
            q.Prompt,
            q.SortOrder,
            q.IsPublished,
            q.AllowsMultiple,
            [.. byQuestion[q.Id].Select(o => new OptionDetail(o.Id, o.Label, o.SortOrder, [.. weights[o.Id].Select(w => new WeightContent(w.PersonaId, w.Weight))]))]))];
    }
}
