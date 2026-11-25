using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

public sealed class SessionAndAppTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly PortalUser User = new(
        "jdoe", "Jane Doe", 42, ["Analyst"], [new ProviderScope("100", "100", ["1", "2"])]);

    private readonly string sessionKey = PortalTestKeys.NewSessionKeyBase64();

    private SessionTokenIssuer Issuer(int lifetimeMinutes = 480, string internalGroup = "") => new(
        Options.Create(new PrismAuthOptions { SessionSigningKeyBase64 = sessionKey, SessionLifetimeMinutes = lifetimeMinutes }),
        Options.Create(new InternalAccessOptions { GroupName = internalGroup }),
        new TestTimeProvider(Now));

    private static bool HasInternalClaim(string token) =>
        new JsonWebTokenHandler().ReadJsonWebToken(token).TryGetClaim(SessionTokenIssuer.InternalClaim, out _);

    [Fact]
    public void Session_token_marks_a_member_of_the_internal_group_as_internal()
    {
        PortalUser member = User with { Groups = ["Other", "emsbilling.com users"] };

        Assert.True(HasInternalClaim(Issuer(internalGroup: "EMSbilling.com Users").Issue(member, [Permissions.App])));
    }

    [Fact]
    public void Session_token_has_no_internal_claim_outside_the_group_or_when_no_group_is_configured()
    {
        PortalUser member = User with { Groups = ["EMSbilling.com Users"] };

        Assert.False(HasInternalClaim(Issuer(internalGroup: "EMSbilling.com Users").Issue(User, [Permissions.App])));
        Assert.False(HasInternalClaim(Issuer(internalGroup: "").Issue(member, [Permissions.App])));
        Assert.False(HasInternalClaim(Issuer(internalGroup: "  ").Issue(member, [Permissions.App])));
    }

    [Fact]
    public async Task Session_token_carries_identity_permissions_and_providers_and_verifies_with_the_session_key()
    {
        string token = Issuer().Issue(User, [Permissions.DashboardView]);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = SessionTokenIssuer.Issuer,
            ValidAudience = SessionTokenIssuer.Audience,
            IssuerSigningKey = SessionTokenIssuer.SigningKey(sessionKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = false,
        });

        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("jdoe", jwt.Subject);
        Assert.Equal("Jane Doe", jwt.GetClaim(SessionTokenIssuer.DisplayNameClaim).Value);
        Assert.Equal(Permissions.DashboardView, jwt.GetClaim(SessionTokenIssuer.PermissionsClaim).Value);
        Assert.Equal("42", jwt.GetClaim(SessionTokenIssuer.PortalUserIdClaim).Value);
        Assert.Equal(Now.AddHours(8), new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Session_token_does_not_verify_with_another_key()
    {
        string token = Issuer().Issue(User, [Permissions.DashboardView]);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = SessionTokenIssuer.Issuer,
            ValidAudience = SessionTokenIssuer.Audience,
            IssuerSigningKey = SessionTokenIssuer.SigningKey(PortalTestKeys.NewSessionKeyBase64()),
            ValidateLifetime = false,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Each_session_token_has_its_own_id()
    {
        SessionTokenIssuer issuer = Issuer();

        string first = new JsonWebToken(issuer.Issue(User, [Permissions.DashboardView])).Id;
        string second = new JsonWebToken(issuer.Issue(User, [Permissions.DashboardView])).Id;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Refuses_a_session_that_will_not_fit_in_a_cookie()
    {
        PortalUser big = User with { Roles = Enumerable.Range(0, 100).Select(i => new string('r', 60) + i).ToList() };

        SessionTooLargeException ex = Assert.Throws<SessionTooLargeException>(() => Issuer().Issue(big, [Permissions.DashboardView]));

        Assert.True(ex.Length > SessionTokenIssuer.MaxTokenLength);
    }

    [Fact]
    public void Providers_and_facilities_never_go_into_the_session_token()
    {
        PortalUser many = User with
        {
            Providers = Enumerable.Range(0, 400).Select(i => new ProviderScope($"{i}", $"Provider {i}", Enumerable.Range(0, 5000).Select(f => $"{f}").ToList())).ToList(),
        };

        string token = Issuer().Issue(many, [Permissions.DashboardView]);

        Assert.True(token.Length < SessionTokenIssuer.MaxTokenLength);
        Assert.DoesNotContain("Provider 1", new JsonWebToken(token).EncodedPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task App_token_to_the_portal_is_rs512_with_the_documented_claims_and_a_five_minute_life()
    {
        using var rsa = RSA.Create(2048);
        var options = Options.Create(new PrismAuthOptions
        {
            ApplicationId = "prism-test",
            AdminPortalAudience = "admin-portal-api-test",
            ApplicationPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
        });

        string token = new PortalAppJwtFactory(options, new TestTimeProvider(Now)).Create();

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "prism-test",
            ValidAudience = "admin-portal-api-test",
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha512],
            ValidateLifetime = false,
        });

        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("prism-test", jwt.Id);
        Assert.Equal(Now.AddMinutes(5), new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero), TimeSpan.FromSeconds(1));
        Assert.Equal(Now.AddMinutes(-1), new DateTimeOffset(jwt.ValidFrom, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }
}
