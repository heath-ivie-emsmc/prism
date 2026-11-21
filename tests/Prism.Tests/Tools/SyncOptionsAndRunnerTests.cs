using Prism.Utility.AdminPortalSetup;

namespace Prism.Tests.Tools;

public sealed class SyncOptionsAndRunnerTests
{
    [Fact]
    public void Defaults_are_dev_and_a_real_publish()
    {
        (SyncOptions? options, string? error) = SyncOptions.Parse([]);

        Assert.Null(error);
        Assert.Equal("Sync", options!.Command.ToString());
        Assert.Equal("dev", options.Environment);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Reads_every_option()
    {
        (SyncOptions? options, string? error) = SyncOptions.Parse(
            ["--environment", "stage", "--application-id", "prism-stage", "--key-file", "k.pem", "--only-roles", "--dry-run"]);

        Assert.Null(error);
        Assert.Equal("stage", options!.Environment);
        Assert.Equal("prism-stage", options.ApplicationId);
        Assert.Equal("k.pem", options.KeyFile);
        Assert.True(options.OnlyRoles);
        Assert.True(options.DryRun);
    }

    [Theory]
    [InlineData("--get-permissions", "GetPermissions")]
    [InlineData("--print-public-key", "PrintPublicKey")]
    [InlineData("--help", "Help")]
    [InlineData("-h", "Help")]
    public void Reads_the_commands(string flag, string expected)
    {
        (SyncOptions? options, _) = SyncOptions.Parse([flag]);

        Assert.Equal(expected, options!.Command.ToString());
    }

    [Theory]
    [InlineData("--evironment")]
    [InlineData("--dry-run=true")]
    [InlineData("stage")]
    public void An_unknown_argument_is_an_error_so_a_typo_cannot_change_what_runs(string arg)
    {
        (SyncOptions? options, string? error) = SyncOptions.Parse([arg]);

        Assert.Null(options);
        Assert.Contains(arg, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--environment")]
    [InlineData("--application-id")]
    [InlineData("--key-file")]
    public void A_missing_value_is_an_error(string flag)
    {
        (SyncOptions? options, string? error) = SyncOptions.Parse([flag]);

        Assert.Null(options);
        Assert.Contains("needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_permissions_and_only_roles_cannot_be_combined()
    {
        (SyncOptions? options, string? error) = SyncOptions.Parse(["--only-permissions", "--only-roles"]);

        Assert.Null(options);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task A_dry_run_succeeds_without_a_key_or_a_network()
    {
        int exitCode = await Runner.RunAsync(["--dry-run"]);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("prod")]
    public async Task Deployed_environments_need_an_explicit_application_id(string environment)
    {
        int exitCode = await Runner.RunAsync(["--environment", environment, "--dry-run"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task An_unknown_environment_is_refused()
    {
        Assert.Equal(2, await Runner.RunAsync(["--environment", "production", "--dry-run"]));
    }

    [Fact]
    public async Task A_publish_without_a_key_file_is_refused_before_any_request()
    {
        Assert.Equal(2, await Runner.RunAsync(["--key-file", "/nonexistent/prism.pem"]));
    }
}
