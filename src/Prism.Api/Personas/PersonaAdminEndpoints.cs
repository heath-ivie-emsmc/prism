using Prism.Api.Admin;
using Prism.Api.SavedViews;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Application.Personas;

namespace Prism.Api.Personas;

/// <summary>
/// Personas and the questionnaire, for Internal Users. Authoring needs the template author tier; publishing needs the template publisher tier.
/// Nothing here reads Provider data or a user's answers.
/// </summary>
internal static class PersonaAdminEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/admin").WithTags("Admin");
        MapPersonas(group);
        MapQuestions(group);
    }

    private static void MapPersonas(RouteGroupBuilder group)
    {
        group.MapGet("/personas", ListPersonas)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("ListPersonas")
            .WithSummary("The Personas")
            .WithDescription("Every Persona by sort order, with the View Template it points to and whether it is published. Needs the template author tier.")
            .Produces<PersonaListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/personas", CreatePersona)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(PersonaLimits.MaxRequestBodyBytes))
            .WithName("CreatePersona")
            .WithSummary("Create a Persona")
            .WithDescription(
                "Creates an unpublished Persona that points to an existing View Template. Example body: `{\"name\":\"Air chief\",\"viewTemplateId\":3,\"sortOrder\":1}`. "
                + "`name` is 1 to 80 characters and unique. `sortOrder` is 0 to 1000 and breaks ties, lowest first. At most 20 Personas. The creation is audited. Needs the template author tier.")
            .Produces<PersonaResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/personas/{personaId:int}", UpdatePersona)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(PersonaLimits.MaxRequestBodyBytes))
            .WithName("UpdatePersona")
            .WithSummary("Save changes to a Persona")
            .WithDescription("Same body as create. A published Persona cannot be pointed at a View Template that is not published (409). The save is audited. Needs the template author tier.")
            .Produces<PersonaResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/personas/{personaId:int}/publish", PublishPersona)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("PublishPersona")
            .WithSummary("Publish a Persona")
            .WithDescription("Makes the Persona selectable by the questionnaire. Its View Template must be published (409 otherwise). Needs the template publisher tier. The change is audited.")
            .Produces<PersonaResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/personas/{personaId:int}/unpublish", UnpublishPersona)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("UnpublishPersona")
            .WithSummary("Take a Persona back to a draft")
            .WithDescription("Users who already have this Persona fall back to the Service Line default home page until it is published again. Needs the template publisher tier. The change is audited.")
            .Produces<PersonaResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/personas/{personaId:int}", DeletePersona)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("DeletePersona")
            .WithSummary("Delete a Persona")
            .WithDescription("Refused with 409 while users have this Persona or a question option weighs it. Unpublish it instead. The deletion is audited. Needs the template author tier.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapQuestions(RouteGroupBuilder group)
    {
        group.MapGet("/onboarding-questions", ListQuestions)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("ListOnboardingQuestions")
            .WithSummary("The questionnaire questions")
            .WithDescription("Every question with its options and the weights each option adds to a Persona. Needs the template author tier.")
            .Produces<QuestionListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/onboarding-questions", CreateQuestion)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(PersonaLimits.MaxRequestBodyBytes))
            .WithName("CreateOnboardingQuestion")
            .WithSummary("Create a questionnaire question")
            .WithDescription(
                "Creates an unpublished question. Set `allowsMultiple` to true for a choose-all-that-apply question (default false). Example body: `{\"prompt\":\"What best describes your role?\",\"sortOrder\":1,\"options\":[{\"label\":\"I run an air program\",\"sortOrder\":1,\"weights\":[{\"personaId\":1,\"weight\":10}]},"
                + "{\"label\":\"I handle billing\",\"sortOrder\":2,\"weights\":[{\"personaId\":2,\"weight\":10}]}]}`. 2 to 8 options. A weight is 0 to 100 and each Persona appears once per option. "
                + "The creation is audited. Needs the template author tier.")
            .Produces<QuestionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/onboarding-questions/{questionId:int}", UpdateQuestion)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithMetadata(new RequestSizeLimitMetadata(PersonaLimits.MaxRequestBodyBytes))
            .WithName("UpdateOnboardingQuestion")
            .WithSummary("Save changes to a question")
            .WithDescription("Same body as create. The options and weights are replaced as sent. The save is audited. Needs the template author tier.")
            .Produces<QuestionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/onboarding-questions/{questionId:int}/publish", PublishQuestion)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("PublishOnboardingQuestion")
            .WithSummary("Publish a question")
            .WithDescription(
                "At most three questions can be published (409 for a fourth). The questionnaire is shown only while two or three are published, each with at least two options, "
                + "and at least one published Persona points to a published View Template; otherwise users are not asked. Needs the template publisher tier. The change is audited.")
            .Produces<QuestionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/onboarding-questions/{questionId:int}/unpublish", UnpublishQuestion)
            .RequireAuthorization(Permissions.AdminTemplatesPublish)
            .WithName("UnpublishOnboardingQuestion")
            .WithSummary("Take a question back to a draft")
            .WithDescription("Needs the template publisher tier. The change is audited.")
            .Produces<QuestionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/onboarding-questions/{questionId:int}", DeleteQuestion)
            .RequireAuthorization(Permissions.AdminTemplatesAuthor)
            .WithName("DeleteOnboardingQuestion")
            .WithSummary("Delete a question")
            .WithDescription("Removes the question with its options and weights. The deletion is audited. Needs the template author tier.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListPersonas(IPersonaStore store, CancellationToken cancellationToken) =>
        Results.Ok(new PersonaListResponse([.. (await store.ListAsync(cancellationToken)).Select(Persona)]));

    private static async Task<IResult> CreatePersona(PersonaRequest? body, HttpContext http, IPersonaStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        if (ValidatePersona(body) is { } invalid)
        {
            return invalid;
        }

        PersonaWriteResult result = await store.CreateAsync(actor, new PersonaContent(body!.Name!.Trim(), body.ViewTemplateId!.Value, body.SortOrder!.Value), cancellationToken);
        return result.Persona is { } persona ? Results.Created($"/api/v1/admin/personas/{persona.Id}", Persona(persona)) : PersonaProblem(result.Status);
    }

    private static async Task<IResult> UpdatePersona(int personaId, PersonaRequest? body, HttpContext http, IPersonaStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        if (ValidatePersona(body) is { } invalid)
        {
            return invalid;
        }

        PersonaWriteResult result = await store.UpdateAsync(actor, personaId, new PersonaContent(body!.Name!.Trim(), body.ViewTemplateId!.Value, body.SortOrder!.Value), cancellationToken);
        return result.Persona is { } persona ? Results.Ok(Persona(persona)) : PersonaProblem(result.Status);
    }

    private static Task<IResult> PublishPersona(int personaId, HttpContext http, IPersonaStore store, CancellationToken cancellationToken) =>
        SetPersonaPublished(personaId, true, http, store, cancellationToken);

    private static Task<IResult> UnpublishPersona(int personaId, HttpContext http, IPersonaStore store, CancellationToken cancellationToken) =>
        SetPersonaPublished(personaId, false, http, store, cancellationToken);

    private static async Task<IResult> SetPersonaPublished(int personaId, bool published, HttpContext http, IPersonaStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        PersonaWriteResult result = await store.SetPublishedAsync(actor, personaId, published, cancellationToken);
        return result.Persona is { } persona ? Results.Ok(Persona(persona)) : PersonaProblem(result.Status);
    }

    private static async Task<IResult> DeletePersona(int personaId, HttpContext http, IPersonaStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        PersonaWriteResult result = await store.DeleteAsync(actor, personaId, cancellationToken);
        return result.Status == PersonaWriteStatus.Saved ? Results.NoContent() : PersonaProblem(result.Status);
    }

    private static async Task<IResult> ListQuestions(IOnboardingQuestionStore store, CancellationToken cancellationToken) =>
        Results.Ok(new QuestionListResponse([.. (await store.ListAsync(cancellationToken)).Select(Question)]));

    private static async Task<IResult> CreateQuestion(QuestionRequest? body, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        if (ValidateQuestion(body) is not { } content)
        {
            return InvalidQuestion();
        }

        QuestionWriteResult result = await store.CreateAsync(actor, content, cancellationToken);
        return result.Question is { } question ? Results.Created($"/api/v1/admin/onboarding-questions/{question.Id}", Question(question)) : QuestionProblem(result.Status);
    }

    private static async Task<IResult> UpdateQuestion(int questionId, QuestionRequest? body, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        if (ValidateQuestion(body) is not { } content)
        {
            return InvalidQuestion();
        }

        QuestionWriteResult result = await store.UpdateAsync(actor, questionId, content, cancellationToken);
        return result.Question is { } question ? Results.Ok(Question(question)) : QuestionProblem(result.Status);
    }

    private static Task<IResult> PublishQuestion(int questionId, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken) =>
        SetQuestionPublished(questionId, true, http, store, cancellationToken);

    private static Task<IResult> UnpublishQuestion(int questionId, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken) =>
        SetQuestionPublished(questionId, false, http, store, cancellationToken);

    private static async Task<IResult> SetQuestionPublished(int questionId, bool published, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        QuestionWriteResult result = await store.SetPublishedAsync(actor, questionId, published, cancellationToken);
        return result.Question is { } question ? Results.Ok(Question(question)) : QuestionProblem(result.Status);
    }

    private static async Task<IResult> DeleteQuestion(int questionId, HttpContext http, IOnboardingQuestionStore store, CancellationToken cancellationToken)
    {
        if (ViewTemplateEndpoints.Actor(http.User) is not { } actor)
        {
            return ViewTemplateEndpoints.NoIdentity();
        }

        QuestionWriteResult result = await store.DeleteAsync(actor, questionId, cancellationToken);
        return result.Status == QuestionWriteStatus.Saved ? Results.NoContent() : QuestionProblem(result.Status);
    }

    /// <summary>Codes only, never the submitted values.</summary>
    private static IResult? ValidatePersona(PersonaRequest? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (body?.Name is not { } name || string.IsNullOrWhiteSpace(name) || name.Trim().Length > PersonaLimits.MaxNameLength)
        {
            errors["name"] = ["invalid_name"];
        }

        if (body?.ViewTemplateId is not > 0)
        {
            errors["viewTemplateId"] = ["invalid_view_template"];
        }

        if (body?.SortOrder is not (>= 0 and <= PersonaLimits.MaxSortOrder))
        {
            errors["sortOrder"] = ["invalid_sort_order"];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    /// <summary>The content, or null when the question or any option or weight is outside the bounds.</summary>
    private static QuestionContent? ValidateQuestion(QuestionRequest? body)
    {
        if (body?.Prompt is not { } prompt || string.IsNullOrWhiteSpace(prompt) || prompt.Trim().Length > PersonaLimits.MaxPromptLength
            || body.SortOrder is not (>= 0 and <= PersonaLimits.MaxSortOrder)
            || body.Options is not { Count: >= PersonaLimits.MinOptions and <= PersonaLimits.MaxOptions } options)
        {
            return null;
        }

        var parsed = new List<OptionContent>(options.Count);
        foreach (OptionRequest option in options)
        {
            if (ValidateOption(option) is not { } content)
            {
                return null;
            }

            parsed.Add(content);
        }

        return new QuestionContent(prompt.Trim(), body.SortOrder.Value, body.AllowsMultiple ?? false, parsed);
    }

    private static OptionContent? ValidateOption(OptionRequest? option)
    {
        if (option?.Label is not { } label || string.IsNullOrWhiteSpace(label) || label.Trim().Length > PersonaLimits.MaxLabelLength
            || option.SortOrder is not (>= 0 and <= PersonaLimits.MaxSortOrder)
            || option.Weights is not { Count: <= PersonaLimits.MaxPersonas } weights)
        {
            return null;
        }

        var parsed = new List<WeightContent>(weights.Count);
        foreach (WeightRequest weight in weights)
        {
            if (weight?.PersonaId is not > 0 || weight.Weight is not (>= 0 and <= PersonaLimits.MaxWeight) || parsed.Any(w => w.PersonaId == weight.PersonaId))
            {
                return null;
            }

            parsed.Add(new WeightContent(weight.PersonaId.Value, weight.Weight.Value));
        }

        return new OptionContent(label.Trim(), option.SortOrder.Value, parsed);
    }

    private static IResult InvalidQuestion() => Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["invalid_question"] });

    private static IResult PersonaProblem(PersonaWriteStatus status) => status switch
    {
        PersonaWriteStatus.NameTaken => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A Persona with this name already exists."),
        PersonaWriteStatus.LimitReached => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The number of Personas has reached its limit."),
        PersonaWriteStatus.InUse => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Users or question options still use this Persona. Unpublish it instead."),
        PersonaWriteStatus.TemplateNotPublished => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A published Persona needs a published View Template."),
        PersonaWriteStatus.TemplateMissing => Results.ValidationProblem(new Dictionary<string, string[]> { ["viewTemplateId"] = ["unknown_view_template"] }),
        _ => Results.NotFound(),
    };

    private static IResult QuestionProblem(QuestionWriteStatus status) => status switch
    {
        QuestionWriteStatus.LimitReached => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The number of questions has reached its limit."),
        QuestionWriteStatus.TooManyPublished => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "At most three questions can be published."),
        QuestionWriteStatus.UnknownPersona => Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["unknown_persona"] }),
        _ => Results.NotFound(),
    };

    private static PersonaResponse Persona(PersonaDetail p) => new(p.Id, p.Name, p.ViewTemplateId, p.ViewTemplateName, p.SortOrder, p.IsPublished);

    private static QuestionResponse Question(QuestionDetail q) =>
        new(q.Id, q.Prompt, q.SortOrder, q.IsPublished, q.AllowsMultiple, [.. q.Options.Select(o => new OptionResponse(o.Id, o.Label, o.SortOrder, [.. o.Weights.Select(w => new WeightResponse(w.PersonaId, w.Weight))]))]);
}

/// <summary>A Persona to save. Only these fields are read: anything else in the body is ignored.</summary>
public sealed record PersonaRequest(string? Name, int? ViewTemplateId, int? SortOrder);

public sealed record PersonaResponse(int Id, string Name, int ViewTemplateId, string? ViewTemplateName, int SortOrder, bool IsPublished);

public sealed record PersonaListResponse(IReadOnlyList<PersonaResponse> Personas);

public sealed record WeightRequest(int? PersonaId, int? Weight);

public sealed record OptionRequest(string? Label, int? SortOrder, IReadOnlyList<WeightRequest>? Weights);

/// <summary>A question to save. Only these fields are read: anything else in the body is ignored.</summary>
public sealed record QuestionRequest(string? Prompt, int? SortOrder, IReadOnlyList<OptionRequest>? Options, bool? AllowsMultiple = null);

public sealed record WeightResponse(int PersonaId, int Weight);

public sealed record OptionResponse(int Id, string Label, int SortOrder, IReadOnlyList<WeightResponse> Weights);

public sealed record QuestionResponse(int Id, string Prompt, int SortOrder, bool IsPublished, bool AllowsMultiple, IReadOnlyList<OptionResponse> Options);

public sealed record QuestionListResponse(IReadOnlyList<QuestionResponse> Questions);
