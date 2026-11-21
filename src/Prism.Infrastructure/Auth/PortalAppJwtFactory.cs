using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>Builds the RS512 token this app presents to the Admin Portal API (issuer is the application id, five-minute life).</summary>
public sealed class PortalAppJwtFactory(IOptions<PrismAuthOptions> options, TimeProvider timeProvider)
{
    public string Create()
    {
        PrismAuthOptions settings = options.Value;
        DateTimeOffset now = timeProvider.GetUtcNow();

        using var rsa = RSA.Create();
        rsa.ImportFromPem(settings.ApplicationPrivateKeyPem);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.ApplicationId,
            Audience = settings.AdminPortalAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = now.AddMinutes(5).UtcDateTime,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Jti] = settings.ApplicationId },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } }, SecurityAlgorithms.RsaSha512),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
