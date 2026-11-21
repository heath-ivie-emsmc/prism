using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>Fails startup when sign-in is not fully configured, so a missing environment file cannot reuse another environment's portal.</summary>
public sealed class PrismAuthOptionsValidator : IValidateOptions<PrismAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, PrismAuthOptions options)
    {
        var failures = new List<string>();

        Require(options.ApplicationId, nameof(options.ApplicationId));
        Require(options.AccountPortalBaseUrl, nameof(options.AccountPortalBaseUrl));
        Require(options.AdminPortalApiBaseUrl, nameof(options.AdminPortalApiBaseUrl));
        Require(options.LoginTokenIssuer, nameof(options.LoginTokenIssuer));
        Require(options.AdminPortalAudience, nameof(options.AdminPortalAudience));
        Require(options.LoginTokenPublicKeyUrl, nameof(options.LoginTokenPublicKeyUrl));
        Require(options.ApplicationPrivateKeyPem, nameof(options.ApplicationPrivateKeyPem));

        RequireHttps(options.AccountPortalBaseUrl, nameof(options.AccountPortalBaseUrl));
        RequireHttps(options.AdminPortalApiBaseUrl, nameof(options.AdminPortalApiBaseUrl));
        RequireHttps(options.LoginTokenPublicKeyUrl, nameof(options.LoginTokenPublicKeyUrl));

        if (!IsValidSigningKey(options.SessionSigningKeyBase64))
        {
            failures.Add($"{nameof(options.SessionSigningKeyBase64)} must be base64 and decode to at least 32 bytes.");
        }

        ValidateLimits(options, failures);

        if (!IsSafeRelativePath(options.PostLoginRedirectUrl))
        {
            failures.Add($"{nameof(options.PostLoginRedirectUrl)} must be a root-relative path such as '/'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Require(string value, string setting)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"{PrismAuthOptions.SectionName}:{setting} is required.");
            }
        }

        void RequireHttps(string value, string setting)
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsHttpsOrLoopback(value))
            {
                failures.Add($"{PrismAuthOptions.SectionName}:{setting} must be an absolute https URL.");
            }
        }
    }

    private static void ValidateLimits(PrismAuthOptions options, List<string> failures)
    {
        if (options.SessionLifetimeMinutes is < 5 or > 1440)
        {
            failures.Add($"{nameof(options.SessionLifetimeMinutes)} must be between 5 and 1440.");
        }

        if (options.MaxLoginTokenLifetimeMinutes is < 1 or > 12)
        {
            failures.Add($"{nameof(options.MaxLoginTokenLifetimeMinutes)} must be between 1 and 12, so a token cannot outlive its replay entry.");
        }

        if (options.RateLimitPerMinute < 1 || options.CallbackRateLimitPerMinute < 1)
        {
            failures.Add("Rate limits must be at least 1 request per minute.");
        }

        if (!string.IsNullOrWhiteSpace(options.LoginReturnUrl) && !IsLoopbackHttp(options.LoginReturnUrl))
        {
            failures.Add($"{PrismAuthOptions.SectionName}:{nameof(options.LoginReturnUrl)} must be an absolute http URL on localhost.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl) && !IsHttpsOrLoopback(options.PublicBaseUrl))
        {
            failures.Add($"{PrismAuthOptions.SectionName}:{nameof(options.PublicBaseUrl)} must be an absolute https URL.");
        }
    }

    private static bool IsLoopbackHttp(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && uri.UserInfo.Length == 0;

    private static bool IsHttpsOrLoopback(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static bool IsValidSigningKey(string value)
    {
        Span<byte> buffer = new byte[Math.Max(value.Length, 1)];
        return Convert.TryFromBase64String(value, buffer, out int written) && written >= 32;
    }

    private static bool IsSafeRelativePath(string value) =>
        value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\');
}
