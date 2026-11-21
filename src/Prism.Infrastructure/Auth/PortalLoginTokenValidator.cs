using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Validates the short-lived JWT the Account Portal sends back after sign-in: RSA signature (PKCS#1 or PSS), exact issuer and
/// audience, expiry with two minutes of clock skew, and a required <c>jti</c> that is the nonce for the user-data exchange.
/// </summary>
public sealed class PortalLoginTokenValidator(
    IPortalPublicKeyProvider keys,
    IOptions<PrismAuthOptions> options,
    TimeProvider timeProvider) : ILoginTokenValidator
{
    public const int MaxTokenLength = 8192;

    private const int MaxTokenIdLength = 128;

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
    ];

    public async Task<ValidatedLoginToken> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            throw new LoginTokenInvalidException(LoginTokenFailure.Invalid);
        }

        PrismAuthOptions settings = options.Value;
        string pem = await keys.GetPublicKeyPemAsync(cancellationToken);

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (ArgumentException)
        {
            throw new LoginTokenInvalidException(LoginTokenFailure.Invalid);
        }

        var parameters = new TokenValidationParameters
        {
            // The RSA instance is disposed when this call ends. With the default factory, the library caches a signature
            // provider keyed by the key's thumbprint, so the next sign-in would reuse a provider holding a disposed key and
            // every sign-in after the first would fail.
            IssuerSigningKey = new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } },
            ValidAlgorithms = AllowedAlgorithms,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuer = settings.LoginTokenIssuer,
            ValidateAudience = true,
            ValidAudience = settings.ApplicationId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            LifetimeValidator = ValidateLifetime,
        };

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            throw new LoginTokenInvalidException(
                result.Exception is SecurityTokenExpiredException ? LoginTokenFailure.Expired : LoginTokenFailure.Invalid);
        }

        var jwt = (JsonWebToken)result.SecurityToken;
        string? tokenId = jwt.Id;
        if (string.IsNullOrWhiteSpace(tokenId) || tokenId.Length > MaxTokenIdLength)
        {
            throw new LoginTokenInvalidException(LoginTokenFailure.Invalid);
        }

        var expiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        if (expiresAt - timeProvider.GetUtcNow() > TimeSpan.FromMinutes(settings.MaxLoginTokenLifetimeMinutes))
        {
            // Replay protection remembers a token only for a bounded window. A token valid for longer could be replayed after it.
            throw new LoginTokenInvalidException(LoginTokenFailure.Invalid);
        }

        return new ValidatedLoginToken(tokenId, expiresAt);
    }

    // The handler reads the system clock, so lifetime is checked here against the injected TimeProvider.
    private bool ValidateLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (expires is null)
        {
            return false;
        }

        if (new DateTimeOffset(expires.Value, TimeSpan.Zero) + ClockSkew <= now)
        {
            throw new SecurityTokenExpiredException("The token has expired.");
        }

        return notBefore is null || new DateTimeOffset(notBefore.Value, TimeSpan.Zero) - ClockSkew <= now;
    }
}
