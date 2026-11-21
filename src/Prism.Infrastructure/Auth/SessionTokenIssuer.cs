using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>Issues the HS256 session JWT that the API puts in an HttpOnly cookie.</summary>
public sealed class SessionTokenIssuer(IOptions<PrismAuthOptions> options, TimeProvider timeProvider) : ISessionTokenIssuer
{
    public const string Issuer = "prism-api";
    public const string Audience = "prism-web";
    public const string SubjectClaim = "sub";
    public const string DisplayNameClaim = "display_name";
    public const string PortalUserIdClaim = "portal_user_id";
    public const string RolesClaim = "roles";
    public const string PermissionsClaim = "permissions";

    /// <summary>Browsers cap a cookie near 4096 bytes including its name and attributes.</summary>
    public const int MaxTokenLength = 3500;

    public static SymmetricSecurityKey SigningKey(string base64) => new(Convert.FromBase64String(base64));

    public string Issue(PortalUser user, IReadOnlyList<string> permissions)
    {
        PrismAuthOptions settings = options.Value;
        DateTimeOffset now = timeProvider.GetUtcNow();

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.UserName,
            [JwtRegisteredClaimNames.Jti] = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            [DisplayNameClaim] = user.DisplayName,
            [RolesClaim] = user.Roles.ToArray(),
            [PermissionsClaim] = permissions.ToArray(),
        };
        if (user.PortalUserId is { } portalId)
        {
            claims[PortalUserIdClaim] = portalId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(settings.SessionLifetimeMinutes).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningKey(settings.SessionSigningKeyBase64), SecurityAlgorithms.HmacSha256),
        };

        string token = new JsonWebTokenHandler().CreateToken(descriptor);
        return token.Length <= MaxTokenLength ? token : throw new SessionTooLargeException(token.Length);
    }
}
