using System.Security.Claims;
using System.Globalization;
using Prism.Api.SavedViews;
using Prism.Application.Auth;
using Prism.Application.Personas;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Personas;

/// <summary>
/// The first-login questionnaire for the signed-in user. The user is always the session's: no route, header or body value can name another user,
/// and the Persona and completion state are never read from a request. Raw answers are scored and discarded.
/// </summary>
internal static class OnboardingEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/me/onboarding")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Onboarding");

        group.MapGet("/", Get)
            .WithName("GetOnboardingQuestionnaire")
            .WithSummary("The first-login questions")
            .WithDescription(
                "The published questions in order, each with its options. Weights are never returned. `questions` is empty when no usable questionnaire is published, "
                + "or it could not be read, and the user is then not asked. Use `onboardingRequired` from the current-user response to decide whether to show it.")
            .Produces<OnboardingResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", Submit)
            .WithMetadata(new RequestSizeLimitMetadata(PersonaLimits.MaxRequestBodyBytes))
            .WithName("SubmitOnboardingAnswers")
            .WithSummary("Answer the first-login questions")
            .WithDescription(
                "Send one answer per published question, or one per chosen option when the question `allowsMultiple`, for example `{\"answers\":[{\"questionId\":1,\"optionId\":3},{\"questionId\":2,\"optionId\":7},{\"questionId\":2,\"optionId\":8}]}`. "
                + "The answers pick one Persona by the total weight of every chosen option, ties going to the lower sort order, and only the Persona and the completion time are stored: the answers are not. "
                + "Answering again replaces the Persona. Returns 400 for a question with no answer, a single-choice question with several, an option chosen twice, or an option that is not in its question, and 409 when no questionnaire is published.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Get(IOnboardingService onboarding, CancellationToken cancellationToken)
    {
        IReadOnlyList<OnboardingQuestionView> questions = await onboarding.GetQuestionnaireAsync(cancellationToken) ?? [];
        return Results.Ok(new OnboardingResponse([.. questions.Select(q => new OnboardingQuestionResponse(q.Id, q.Prompt, q.AllowsMultiple, [.. q.Options.Select(o => new OnboardingOptionResponse(o.Id, o.Label))]))]));
    }

    private static async Task<IResult> Submit(OnboardingSubmitRequest? body, HttpContext http, IOnboardingService onboarding, CancellationToken cancellationToken)
    {
        if (!long.TryParse(http.User.FindFirstValue(SessionTokenIssuer.PortalUserIdClaim), CultureInfo.InvariantCulture, out long portalUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This session has no user to save to.");
        }

        if (body?.Answers is not { Count: > 0 and <= PersonaLimits.MaxAnswers } answers || answers.Any(a => a?.QuestionId is not > 0 || a.OptionId is not > 0))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = ["invalid_answers"] });
        }

        OnboardingSubmitStatus status = await onboarding.SubmitAsync(portalUserId, [.. answers.Select(a => new OnboardingAnswer(a.QuestionId!.Value, a.OptionId!.Value))], cancellationToken);
        return status switch
        {
            OnboardingSubmitStatus.Saved => Results.NoContent(),
            OnboardingSubmitStatus.NotActive => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "There is no questionnaire to answer."),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = ["invalid_answers"] }),
        };
    }
}

public sealed record OnboardingOptionResponse(int Id, string Label);

/// <summary>A question. When <c>allowsMultiple</c> is true the user chooses all that apply (at least one), otherwise exactly one.</summary>
public sealed record OnboardingQuestionResponse(int Id, string Prompt, bool AllowsMultiple, IReadOnlyList<OnboardingOptionResponse> Options);

public sealed record OnboardingResponse(IReadOnlyList<OnboardingQuestionResponse> Questions);

public sealed record OnboardingAnswerRequest(int? QuestionId, int? OptionId);

/// <summary>The answers. Only <c>answers</c> is read: a Persona or completion value in the body is ignored.</summary>
public sealed record OnboardingSubmitRequest(IReadOnlyList<OnboardingAnswerRequest>? Answers);
