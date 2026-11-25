namespace Prism.Application.Admin;

/// <summary>The kinds of admin action that leave an audit record.</summary>
public static class AdminAuditActions
{
    public const string TemplateCreated = "view_template.created";
    public const string TemplateUpdated = "view_template.updated";
    public const string TemplatePublished = "view_template.published";
    public const string TemplateUnpublished = "view_template.unpublished";
    public const string TemplateDeleted = "view_template.deleted";
    public const string AssignmentCreated = "template_assignment.created";
    public const string AssignmentRevoked = "template_assignment.revoked";
    public const string ProviderPreviewed = "provider_preview.opened";
    public const string PersonaCreated = "persona.created";
    public const string PersonaUpdated = "persona.updated";
    public const string PersonaPublished = "persona.published";
    public const string PersonaUnpublished = "persona.unpublished";
    public const string PersonaDeleted = "persona.deleted";
    public const string CustomerRoleChanged = "customer_role.changed";
    public const string CustomerSettingsChanged = "customer_settings.changed";
    public const string QuestionCreated = "onboarding_question.created";
    public const string QuestionUpdated = "onboarding_question.updated";
    public const string QuestionPublished = "onboarding_question.published";
    public const string QuestionUnpublished = "onboarding_question.unpublished";
    public const string QuestionDeleted = "onboarding_question.deleted";
}

/// <summary>The kinds of object an admin action touches.</summary>
public static class AdminAuditObjects
{
    public const string ViewTemplate = "view_template";
    public const string TemplateAssignment = "template_assignment";
    public const string Provider = "provider";
    public const string Persona = "persona";
    public const string ProviderUser = "provider_user";
    public const string ProviderSettings = "provider_settings";
    public const string OnboardingQuestion = "onboarding_question";
}

/// <summary>The Internal User acting. Built by server code from the session, never from a request.</summary>
public sealed record AdminActor(long PortalUserId, string UserName);

/// <summary>What one admin action touched. Codes and counts only: never a name, a layout or free text.</summary>
public sealed record AdminAuditWrite(string Action, string ObjectType, int? ObjectId = null, int? ProviderId = null, int? AffectedCount = null, long? TargetPortalUserId = null, string? Detail = null);

// The audit record is written by the same unit of work as the action it describes, so an action that cannot be audited does not happen.
