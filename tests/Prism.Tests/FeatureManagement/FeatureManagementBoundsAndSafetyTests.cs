using System.Text;
using System.Text.Json;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

/// <summary>AGENTS.md sections 3, 4 and 7 for feature management: what a runtime profile may set, how big it may be, and where the sidecar may live.</summary>
public sealed class FeatureManagementBoundsAndSafetyTests
{
    private static JsonElement Parse(string json) => JsonElement.Parse(json);

    /// <summary>A freeform profile nests its keys ("A:B" is {"A":{"B":...}}); a ":" inside a property name is itself refused as malformed.</summary>
    private static string Nest(string key, string value)
    {
        string[] parts = key.Split(':');
        return string.Concat(parts.Select(part => $"{{\"{part}\":")) + $"\"{value}\"" + new string('}', parts.Length);
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("Aws:SecretsLoader:Enabled")]
    [InlineData("FeatureManagement:Enabled")]
    [InlineData("FeatureFlags:Anything")]
    [InlineData("Serilog:MinimumLevel:Default")]
    [InlineData("ForwardedHeaders:KnownProxies:0")]
    [InlineData("Internal:GroupName")]
    [InlineData("Warehouse:SkipLoginCheck")]
    [InlineData("AllowedHosts")]
    public void A_freeform_profile_cannot_set_a_key_that_is_reviewed_as_code(string key)
    {
        JsonElement root = Parse(Nest(key, "x"));

        SidecarSettingRefusedException ex = Assert.Throws<SidecarSettingRefusedException>(() => SidecarJsonParser.ParseAppSettings(root));

        Assert.Contains("not allowed to be set at runtime", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_freeform_profile_is_refused_whole_never_partly_applied()
    {
        JsonElement root = Parse("""{"Demo":{"First":"1"},"Aws":{"SecretsLoader":{"Enabled":"false"}}}""");

        Assert.Throws<SidecarSettingRefusedException>(() => SidecarJsonParser.ParseAppSettings(root));
    }

    [Fact]
    public void A_freeform_profile_with_ordinary_keys_is_accepted()
    {
        IReadOnlyDictionary<string, string?> parsed = SidecarJsonParser.ParseAppSettings(Parse("""{"Demo":{"Limit":"5"},"Other":{"Key":"v"}}"""));

        Assert.Equal("5", parsed["Demo:Limit"]);
        Assert.Equal("v", parsed["Other:Key"]);
    }

    [Fact]
    public void A_refused_key_name_is_capped_in_the_message()
    {
        string longKey = "Aws:" + new string('k', 5000);

        var ex = new SidecarSettingRefusedException(longKey, "Aws:");

        Assert.True(ex.Message.Length < 300);
    }

    [Fact]
    public void A_profile_with_more_entries_than_the_cap_is_refused()
    {
        string many = "{\"Demo\":{" + string.Join(",", Enumerable.Range(0, SidecarJsonParser.MaxEntries + 1).Select(i => $"\"K{i}\":\"v\"")) + "}}";

        Assert.Throws<JsonException>(() => SidecarJsonParser.ParseAppSettings(Parse(many)));
    }

    [Fact]
    public void A_feature_profile_with_more_flags_than_the_cap_is_refused()
    {
        string many = "{" + string.Join(",", Enumerable.Range(0, SidecarJsonParser.MaxEntries + 1).Select(i => $"\"f{i}\":{{\"enabled\":true}}")) + "}";

        Assert.Throws<JsonException>(() => SidecarJsonParser.ParseFeatures(Parse(many)));
    }

    [Fact]
    public async Task A_sidecar_response_over_the_size_cap_is_refused_before_it_is_parsed()
    {
        byte[] huge = Encoding.UTF8.GetBytes("{\"a\":\"" + new string('x', BoundedSidecarJson.MaxBytes) + "\"}");
        using var stream = new MemoryStream(huge);

        await Assert.ThrowsAsync<InvalidOperationException>(() => BoundedSidecarJson.ParseAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_sidecar_response_under_the_cap_is_parsed()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"values":{"f":{"enabled":true}}}"""));

        using JsonDocument document = await BoundedSidecarJson.ParseAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(document.RootElement.TryGetProperty("values", out _));
    }

    [Theory]
    [InlineData("http://localhost:2772", true)]
    [InlineData("http://127.0.0.1:2772", true)]
    [InlineData("http://[::1]:2772", true)]
    [InlineData("https://appconfig.example.com", true)]
    [InlineData("http://appconfig.internal:2772", false)]
    [InlineData("http://10.0.0.5:2772", false)]
    [InlineData("ftp://localhost", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void The_sidecar_address_is_https_or_http_to_this_machine_only(string baseUrl, bool accepted) =>
        Assert.Equal(accepted, FeatureManagementSidecarRegistration.IsLoopbackOrHttps(baseUrl));

    // Cross-Provider shape (AGENTS.md section 2) for flags: a flag aimed at Provider 1 is off for Provider 2 and for a request with no Provider at all.
    [Fact]
    public async Task A_flag_for_one_provider_is_off_for_another_and_for_no_provider()
    {
        FeatureValue flag = new()
        {
            FeatureName = "Pilot",
            Enabled = true,
            Settings = new Dictionary<string, JsonElement> { ["provider_allowlist"] = Parse("[1]") },
        };
        var manager = new FixedFlags(flag);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.True(await manager.IsEnabledForProviderAsync("Pilot", 1, ct));
        Assert.False(await manager.IsEnabledForProviderAsync("Pilot", 2, ct));
        Assert.False(await manager.IsEnabledAsync("Pilot", FeatureContext.Empty, ct));
        Assert.False(await manager.IsEnabledAsync("Unknown", FeatureContext.Empty, ct));
    }

    [Theory]
    [InlineData("""{"provider_allowlist":"1"}""")]
    [InlineData("""{"provider_allowlist":[1,"2"]}""")]
    [InlineData("""{"provider_allowlist":[1],"targeting_strategy":"maybe"}""")]
    [InlineData("""{"user_allowlist":{"a":1}}""")]
    public async Task A_malformed_targeting_setting_turns_the_flag_off_for_everyone(string settingsJson)
    {
        Dictionary<string, JsonElement> settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settingsJson)!;
        var manager = new FixedFlags(new FeatureValue { FeatureName = "Pilot", Enabled = true, Settings = settings });

        Assert.False(await manager.IsEnabledAsync("Pilot", new FeatureContext { ProviderId = 1, UserId = 1 }, TestContext.Current.CancellationToken));
    }

    private sealed class FixedFlags(FeatureValue flag) : IFeatureValueManager
    {
        public Task<FeatureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureValue?> GetFeatureValueAsync(string featureName, CancellationToken cancellationToken = default) =>
            GetFeatureValueAsync(featureName, FeatureContext.Empty, cancellationToken);

        public Task<FeatureValue?> GetFeatureValueAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<FeatureValue?>(string.Equals(featureName, flag.FeatureName, StringComparison.Ordinal)
                ? flag with { Enabled = FeatureFlagEvaluation.EvaluateEnabled(flag, context) }
                : null);

        public Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default) =>
            IsEnabledAsync(featureName, FeatureContext.Empty, cancellationToken);

        public async Task<bool> IsEnabledAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default) =>
            (await GetFeatureValueAsync(featureName, context, cancellationToken))?.Enabled ?? false;
    }
}
