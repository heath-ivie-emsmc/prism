using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

public sealed class PortalLoginTokenValidatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly PortalTestKeys portal = new();
    private readonly TestTimeProvider clock = new(Now);
    private readonly PortalLoginTokenValidator validator;

    public PortalLoginTokenValidatorTests()
    {
        var options = Options.Create(new PrismAuthOptions
        {
            ApplicationId = PortalTestKeys.ApplicationId,
            LoginTokenIssuer = PortalTestKeys.Issuer,
        });
        validator = new PortalLoginTokenValidator(new FixedKeyProvider(portal.PublicKeyPem), options, clock);
    }

    public void Dispose() => portal.Dispose();

    [Fact]
    public async Task Accepts_a_valid_token_and_returns_the_nonce()
    {
        ValidatedLoginToken result = await validator.ValidateAsync(portal.Mint(Now), TestContext.Current.CancellationToken);

        Assert.Equal("nonce-1", result.TokenId);
        Assert.Equal(Now.AddMinutes(3), result.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Rejects_a_token_that_lives_longer_than_the_replay_window_can_cover()
    {
        // Replay entries are kept for at most 15 minutes. A token valid for an hour could be replayed once its entry is gone.
        string token = portal.Mint(Now, new LoginTokenSpec { ExpiresIn = TimeSpan.FromHours(1) });

        await AssertRejected(token, LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Accepts_a_token_at_the_configured_maximum_life()
    {
        string token = portal.Mint(Now, new LoginTokenSpec { ExpiresIn = TimeSpan.FromMinutes(10) });

        ValidatedLoginToken result = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal("nonce-1", result.TokenId);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.RsaSha256)]
    [InlineData(SecurityAlgorithms.RsaSha384)]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha256)]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha512)]
    public async Task Accepts_the_allowed_rsa_algorithms(string algorithm)
    {
        string token = portal.Mint(Now, new LoginTokenSpec { Algorithm = algorithm });

        ValidatedLoginToken result = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal("nonce-1", result.TokenId);
    }

    [Fact]
    public async Task Rejects_the_wrong_issuer()
    {
        await AssertRejected(portal.Mint(Now, new LoginTokenSpec { Issuer = "someone-else" }), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_a_token_for_another_application()
    {
        await AssertRejected(portal.Mint(Now, new LoginTokenSpec { Audience = "m6" }), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_a_token_without_a_nonce()
    {
        await AssertRejected(portal.Mint(Now, new LoginTokenSpec { TokenId = null }), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_an_oversized_nonce()
    {
        await AssertRejected(portal.Mint(Now, new LoginTokenSpec { TokenId = new string('x', 129) }), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Reports_an_expired_token_as_expired()
    {
        string token = portal.Mint(Now, new LoginTokenSpec { ExpiresIn = TimeSpan.FromMinutes(1) });
        clock.Advance(TimeSpan.FromMinutes(4));

        await AssertRejected(token, LoginTokenFailure.Expired);
    }

    [Fact]
    public async Task Allows_up_to_two_minutes_of_clock_skew_after_expiry()
    {
        string token = portal.Mint(Now, new LoginTokenSpec { ExpiresIn = TimeSpan.FromMinutes(1) });
        clock.Advance(TimeSpan.FromMinutes(2));

        ValidatedLoginToken result = await validator.ValidateAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal("nonce-1", result.TokenId);
    }

    [Fact]
    public async Task Rejects_a_token_not_yet_valid_beyond_the_skew()
    {
        await AssertRejected(
            portal.Mint(Now, new LoginTokenSpec { NotBeforeOffset = TimeSpan.FromMinutes(10), ExpiresIn = TimeSpan.FromMinutes(20) }),
            LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_a_token_signed_by_a_different_key()
    {
        using var other = new PortalTestKeys();

        await AssertRejected(other.Mint(Now), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_a_tampered_payload()
    {
        string[] parts = portal.Mint(Now).Split('.');
        string forged = Base64UrlEncoder.Encode("""{"iss":"account-portal-test","aud":"prism-test","jti":"attacker","exp":9999999999}""");

        await AssertRejected($"{parts[0]}.{forged}.{parts[2]}", LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_algorithm_confusion_with_the_public_key_as_an_hmac_secret()
    {
        var hmacKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(portal.PublicKeyPem));
        string token = portal.Mint(Now, new LoginTokenSpec { Algorithm = SecurityAlgorithms.HmacSha256, Key = hmacKey });

        await AssertRejected(token, LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_an_unsigned_token()
    {
        string header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        string payload = Base64UrlEncoder.Encode("""{"iss":"account-portal-test","aud":"prism-test","jti":"x","exp":9999999999}""");

        await AssertRejected($"{header}.{payload}.", LoginTokenFailure.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task Rejects_garbage(string token)
    {
        await AssertRejected(token, LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_a_token_over_the_length_cap()
    {
        await AssertRejected(new string('a', PortalLoginTokenValidator.MaxTokenLength + 1), LoginTokenFailure.Invalid);
    }

    [Fact]
    public async Task Rejects_when_the_portal_key_is_not_a_valid_pem()
    {
        var broken = new PortalLoginTokenValidator(
            new FixedKeyProvider("not a pem"),
            Options.Create(new PrismAuthOptions { ApplicationId = PortalTestKeys.ApplicationId, LoginTokenIssuer = PortalTestKeys.Issuer }),
            clock);

        LoginTokenInvalidException ex = await Assert.ThrowsAsync<LoginTokenInvalidException>(
            () => broken.ValidateAsync(portal.Mint(Now), TestContext.Current.CancellationToken));

        Assert.Equal(LoginTokenFailure.Invalid, ex.Failure);
    }

    private async Task AssertRejected(string token, LoginTokenFailure expected)
    {
        LoginTokenInvalidException ex = await Assert.ThrowsAsync<LoginTokenInvalidException>(
            () => validator.ValidateAsync(token, TestContext.Current.CancellationToken));

        Assert.Equal(expected, ex.Failure);
    }

    private sealed class FixedKeyProvider(string pem) : IPortalPublicKeyProvider
    {
        public Task<string> GetPublicKeyPemAsync(CancellationToken cancellationToken) => Task.FromResult(pem);
    }
}
