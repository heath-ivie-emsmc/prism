using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Admin;

/// <summary>Composition and routes for the internal admin area.</summary>
public static class AdminServiceCollectionExtensions
{
    /// <summary>Policy for any Internal User, whatever their tier.</summary>
    public const string InternalPolicy = "prism.admin";

    public static IServiceCollection AddPrismAdmin(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<InternalAccessOptions>, InternalAccessOptionsValidator>();
        services.AddOptions<InternalAccessOptions>()
            .BindConfiguration(InternalAccessOptions.SectionName)
            .ValidateOnStart();

        services.AddScoped<IViewTemplateStore, ViewTemplateStore>();
        services.AddScoped<ITemplateAssignmentStore, TemplateAssignmentStore>();
        services.AddScoped<ITemplateDelivery, TemplateDeliveryService>();
        services.AddSingleton<PreviewAuditGate>();
        services.AddScoped<IPreviewAudit, PreviewAudit>();
        services.AddSingleton<IProviderListSource, Prism.Infrastructure.Warehouse.WarehouseProviderList>();
        services.AddSingleton<ProviderCatalogCache>();
        services.AddScoped<IProviderCatalog, ProviderCatalog>();
        services.AddHostedService<ProviderCatalogWarmer>();

        services.AddSingleton<IAuthorizationHandler, InternalTierHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(InternalPolicy, policy => policy.AddRequirements(new InternalTierRequirement(null)))
            .AddPolicy(Permissions.AdminTemplatesAuthor, policy => policy.AddRequirements(new InternalTierRequirement(Permissions.AdminTemplatesAuthor)))
            .AddPolicy(Permissions.AdminTemplatesPublish, policy => policy.AddRequirements(new InternalTierRequirement(Permissions.AdminTemplatesPublish)))
            .AddPolicy(Permissions.AdminCustomersView, policy => policy.AddRequirements(new InternalTierRequirement(Permissions.AdminCustomersView)))
            .AddPolicy(Permissions.AdminManage, policy => policy.AddRequirements(new InternalTierRequirement(Permissions.AdminManage)));
        return services;
    }

    public static void MapAdminEndpoints(this RouteGroupBuilder v1)
    {
        AdminSessionEndpoint.Map(v1);
        ViewTemplateEndpoints.Map(v1);
        ProviderSearchEndpoint.Map(v1);
    }
}

/// <summary>An admin tier (or, when null, plain internal membership). Both the group and the tier are required.</summary>
public sealed record InternalTierRequirement(string? Tier) : IAuthorizationRequirement;

/// <summary>
/// Passes only when the session was issued to a member of the internal group and, for a tier, carries that tier permission.
/// The internal claim is set by the server at sign-in. A role without the group, or the group without a tier, is refused.
/// </summary>
public sealed class InternalTierHandler : AuthorizationHandler<InternalTierRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, InternalTierRequirement requirement)
    {
        bool internalUser = context.User.HasClaim(SessionTokenIssuer.InternalClaim, "true");
        bool hasTier = requirement.Tier is null
            || context.User.HasClaim(SessionTokenIssuer.PermissionsClaim, requirement.Tier);
        if (internalUser && hasTier)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The internal group name is optional (the marker role is the normal signal), but it is length-capped.</summary>
internal sealed class InternalAccessOptionsValidator : IValidateOptions<InternalAccessOptions>
{
    public ValidateOptionsResult Validate(string? name, InternalAccessOptions options)
    {
        var problems = new List<string>();
        if (options.GroupName.Length > InternalAccessOptions.MaxGroupNameLength)
        {
            problems.Add($"Internal:GroupName is longer than {InternalAccessOptions.MaxGroupNameLength} characters.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>What the admin area needs to know about the caller. Read from the session only.</summary>
internal static class AdminSessionEndpoint
{
    public static void Map(RouteGroupBuilder v1) =>
        v1.MapGet("/admin/session", Handle)
            .RequireAuthorization(AdminServiceCollectionExtensions.InternalPolicy)
            .WithName("GetAdminSession")
            .WithTags("Admin")
            .WithSummary("The signed-in Internal User's admin tiers")
            .WithDescription("Returns the admin tier permissions the session holds. Requires membership of the internal portal group. A user outside the group, or a customer user, gets 403.")
            .Produces<AdminSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static IResult Handle(ClaimsPrincipal user)
    {
        string[] tiers = user.FindAll(SessionTokenIssuer.PermissionsClaim)
            .Select(claim => claim.Value)
            .Where(Permissions.AdminTiers.Contains)
            .ToArray();
        return Results.Ok(new AdminSessionResponse(tiers));
    }
}

/// <summary>Response of <c>GET /api/v1/admin/session</c>.</summary>
public sealed record AdminSessionResponse(IReadOnlyList<string> Tiers);
