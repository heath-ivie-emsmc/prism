using Prism.Api.Auth;

namespace Prism.Tests.Auth;

public sealed class RedirectTargetsTests
{
    /// <summary>Builds a string with a character that is awkward to type in source, such as a tab or a zero-width space.</summary>
    private static string With(int code, string before = "/", string after = "/evil.com") =>
        before + char.ConvertFromUtf32(code) + after;

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/dashboard?period=2026-10")]
    [InlineData("/help#metrics")]
    [InlineData("/loginx")]
    public void Accepts_safe_internal_paths(string candidate)
    {
        Assert.Equal(candidate, RedirectTargets.TryNormalize(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("/\\evil")]
    [InlineData("/\\\\evil")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=/dashboard")]
    [InlineData("/login/sso")]
    [InlineData("@evil")]
    [InlineData("/@evil.com")]
    [InlineData("relative/path")]
    public void Rejects_open_redirect_style_values(string? candidate)
    {
        Assert.Null(RedirectTargets.TryNormalize(candidate));
    }

    [Theory]
    [InlineData(0x09)]
    [InlineData(0x0a)]
    [InlineData(0x0d)]
    [InlineData(0x00)]
    [InlineData(0x1f)]
    [InlineData(0x7f)]
    [InlineData(0x85)]
    [InlineData(0x200b)]
    [InlineData(0x2028)]
    [InlineData(0xfeff)]
    public void Rejects_control_and_invisible_characters_that_browsers_strip(int code)
    {
        Assert.Null(RedirectTargets.TryNormalize(With(code)));
        Assert.Null(RedirectTargets.TryNormalize(With(code, before: "", after: "/dashboard")));
        Assert.Null(RedirectTargets.TryNormalize(With(code, before: "/dashboard", after: "")));
    }

    [Fact]
    public void Trims_plain_spaces_at_the_ends_but_rejects_one_inside()
    {
        Assert.Equal("/dashboard", RedirectTargets.TryNormalize("  /dashboard "));
        Assert.Null(RedirectTargets.TryNormalize(With(0x20)));
    }

    [Fact]
    public void Rejects_values_over_the_length_cap()
    {
        Assert.Null(RedirectTargets.TryNormalize("/" + new string('a', 3000)));
    }
}
