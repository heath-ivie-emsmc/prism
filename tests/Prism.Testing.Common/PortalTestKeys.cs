using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Prism.Testing.Common;

/// <summary>A throwaway Account Portal: an RSA key pair plus a way to mint login tokens the way the portal does.</summary>
public sealed class PortalTestKeys : IDisposable
{
    public const string Issuer = "account-portal-test";
    public const string ApplicationId = "prism-test";

    private readonly RSA rsa = RSA.Create(2048);

    public string PublicKeyPem => rsa.ExportSubjectPublicKeyInfoPem();

    /// <summary>A private key in PEM, for the app's own signing key setting.</summary>
    public static string NewPrivateKeyPem()
    {
        using var key = RSA.Create(2048);
        return key.ExportPkcs8PrivateKeyPem();
    }

    public static string NewSessionKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public RsaSecurityKey SigningKey() => new(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };

    /// <summary>Mints a login token. The defaults make a valid token; override one thing in <paramref name="spec"/> to break it.</summary>
    public string Mint(DateTimeOffset now, LoginTokenSpec? spec = null)
    {
        spec ??= new LoginTokenSpec();
        var claims = new Dictionary<string, object>();
        if (spec.TokenId is not null)
        {
            claims[JwtRegisteredClaimNames.Jti] = spec.TokenId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer,
            Audience = spec.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = (now + spec.NotBeforeOffset).UtcDateTime,
            Expires = (now + spec.ExpiresIn).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(spec.Key ?? SigningKey(), spec.Algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Dispose() => rsa.Dispose();
}

/// <summary>The knobs for <see cref="PortalTestKeys.Mint"/>. Defaults produce a valid token.</summary>
public sealed record LoginTokenSpec
{
    public string? Issuer { get; init; } = PortalTestKeys.Issuer;

    public string? Audience { get; init; } = PortalTestKeys.ApplicationId;

    public string? TokenId { get; init; } = "nonce-1";

    public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan NotBeforeOffset { get; init; } = TimeSpan.FromSeconds(-30);

    public string Algorithm { get; init; } = SecurityAlgorithms.RsaSha512;

    public SecurityKey? Key { get; init; }
}
