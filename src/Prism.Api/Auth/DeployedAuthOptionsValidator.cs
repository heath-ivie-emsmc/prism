using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Api.Auth;

/// <summary>Extra sign-in checks for deployed environments: no plain http anywhere, and a known public origin for logout.</summary>
public sealed class DeployedAuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<PrismAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, PrismAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!environment.IsDeployedEnvironment())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        foreach ((string setting, string value) in new[]
        {
            (nameof(options.AccountPortalBaseUrl), options.AccountPortalBaseUrl),
            (nameof(options.AdminPortalApiBaseUrl), options.AdminPortalApiBaseUrl),
            (nameof(options.LoginTokenPublicKeyUrl), options.LoginTokenPublicKeyUrl),
            (nameof(options.PublicBaseUrl), options.PublicBaseUrl),
        })
        {
            if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{PrismAuthOptions.SectionName}:{setting} must be an https URL in a deployed environment.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.LoginReturnUrl))
        {
            failures.Add($"{PrismAuthOptions.SectionName}:{nameof(options.LoginReturnUrl)} is for local development and cannot be set in a deployed environment.");
        }

        if (options.RequireSecureCookies == false)
        {
            failures.Add($"{PrismAuthOptions.SectionName}:{nameof(options.RequireSecureCookies)} cannot be false in a deployed environment.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
