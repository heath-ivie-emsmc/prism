using Prism.Infrastructure.SavedViews;

namespace Prism.Tests.SavedViews;

public sealed class SavedViewInvitationOptionsTests
{
    private static SavedViewInvitationOptions Good() => new()
    {
        Enabled = true,
        ExpiryDays = 7,
        PublicBaseUrl = "https://prism.example.org",
        SmtpHost = "smtp.example.org",
        FromAddress = "prism@example.org",
    };

    [Fact]
    public void Off_by_default_so_a_host_with_no_mail_settings_still_starts()
    {
        Assert.False(new SavedViewInvitationOptions().Enabled);
        Assert.Empty(SavedViewInvitationOptionsRules.Problems(new SavedViewInvitationOptions(), deployed: true));
    }

    [Fact]
    public void A_complete_setup_is_valid_everywhere()
    {
        Assert.Empty(SavedViewInvitationOptionsRules.Problems(Good(), deployed: true));
        Assert.Empty(SavedViewInvitationOptionsRules.Problems(Good(), deployed: false));
    }

    [Fact]
    public void An_enabled_setup_without_a_host_address_or_sender_is_refused()
    {
        SavedViewInvitationOptions empty = new() { Enabled = true };

        IReadOnlyList<string> problems = SavedViewInvitationOptionsRules.Problems(empty, deployed: true);

        Assert.Contains(problems, p => p.Contains("PublicBaseUrl", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("SmtpHost", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("FromAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void A_deployed_environment_needs_an_https_link_but_a_developer_machine_may_use_http()
    {
        SavedViewInvitationOptions options = Good();
        options.PublicBaseUrl = "http://localhost:4200";

        Assert.NotEmpty(SavedViewInvitationOptionsRules.Problems(options, deployed: true));
        Assert.Empty(SavedViewInvitationOptionsRules.Problems(options, deployed: false));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example")]
    [InlineData("ftp://prism.example.org")]
    [InlineData("not a url")]
    public void A_base_address_that_is_not_http_or_https_is_refused_even_on_a_developer_machine(string url)
    {
        SavedViewInvitationOptions options = Good();
        options.PublicBaseUrl = url;

        Assert.NotEmpty(SavedViewInvitationOptionsRules.Problems(options, deployed: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(-5)]
    public void The_expiry_is_between_one_and_thirty_days(int days)
    {
        SavedViewInvitationOptions options = Good();
        options.ExpiryDays = days;

        Assert.NotEmpty(SavedViewInvitationOptionsRules.Problems(options, deployed: true));
    }
}
