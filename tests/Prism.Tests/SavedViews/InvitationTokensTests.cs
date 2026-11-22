using Prism.Application.SavedViews;

namespace Prism.Tests.SavedViews;

public sealed class InvitationTokensTests
{
    [Fact]
    public void A_new_token_is_well_formed_url_safe_and_different_each_time()
    {
        string[] tokens = [.. Enumerable.Range(0, 50).Select(_ => InvitationTokens.NewToken())];

        Assert.All(tokens, token =>
        {
            Assert.True(InvitationTokens.IsWellFormed(token));
            Assert.Equal(InvitationTokens.TokenLength, token.Length);
        });
        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789012")] // 43 ok shape but see below for 42 and 44
    public void Shape_check_accepts_only_exactly_the_token_length_and_alphabet(string? value)
    {
        bool expected = value is { Length: InvitationTokens.TokenLength };

        Assert.Equal(expected, InvitationTokens.IsWellFormed(value));
    }

    [Theory]
    [InlineData("0123456789012345678901234567890123456789 12")]
    [InlineData("01234567890123456789012345678901234567890+1")]
    [InlineData("01234567890123456789012345678901234567890/1")]
    [InlineData("01234567890123456789012345678901234567890=1")]
    [InlineData("0123456789012345678901234567890123456789012\n")]
    public void Characters_outside_the_url_safe_alphabet_or_extra_length_are_refused(string value) =>
        Assert.False(InvitationTokens.IsWellFormed(value));

    [Fact]
    public void A_hash_is_64_hex_characters_stable_and_not_the_token()
    {
        string token = InvitationTokens.NewToken();

        string hash = InvitationTokens.Hash(token);

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, InvitationTokens.Hash(token));
        Assert.NotEqual(hash, InvitationTokens.Hash(token + "x"));
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  Someone@Example.ORG ", "someone@example.org")]
    [InlineData("a.b+c@sub.example.org", "a.b+c@sub.example.org")]
    public void A_good_address_is_trimmed_and_lower_cased(string input, string expected) =>
        Assert.Equal(expected, InvitationEmails.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@example.org")]
    [InlineData("two@@example.org")]
    [InlineData("a@b@example.org")]
    [InlineData("a@nodot")]
    [InlineData("a b@example.org")]
    [InlineData("a@example.org, b@example.org")]
    [InlineData("a@example.org;b@example.org")]
    [InlineData("\"quoted\"@example.org")]
    [InlineData("<a@example.org>")]
    [InlineData("a@example.org\r\nBcc: x@example.org")]
    public void A_bad_address_or_one_with_header_injection_is_refused(string? input) =>
        Assert.Null(InvitationEmails.Normalize(input));

    [Fact]
    public void An_address_over_the_length_cap_is_refused() =>
        Assert.Null(InvitationEmails.Normalize(new string('a', SavedViewLimits.MaxEmailLength) + "@example.org"));
}
