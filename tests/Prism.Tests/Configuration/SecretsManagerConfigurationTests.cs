using Microsoft.Extensions.Configuration;
using Prism.Infrastructure.Configuration;

namespace Prism.Tests.Configuration;

public sealed class SecretsManagerConfigurationTests
{
    private sealed class FakeStore(Dictionary<string, string> secrets) : ISecretStore
    {
        public List<string> Asked { get; } = [];

        public string GetSecretString(string secretId)
        {
            Asked.Add(secretId);
            return secrets.TryGetValue(secretId, out string? text) ? text : throw new InvalidOperationException("no such secret");
        }
    }

    private static IConfiguration Load(FakeStore store, params string[] ids) =>
        new ConfigurationBuilder().Add(new SecretsManagerConfigurationSource(store, ids)).Build();

    [Fact]
    public void Keys_in_a_secret_become_configuration_values()
    {
        var store = new FakeStore(new Dictionary<string, string>
        {
            ["prism/stage/app"] = """{"Auth:SessionSigningKeyBase64":"abc","ConnectionStrings":{"Prism":"Host=db","Redis":"cache:6379"},"Auth__ApplicationPrivateKeyPem":"-----BEGIN-----"}""",
        });

        IConfiguration config = Load(store, "prism/stage/app");

        Assert.Equal("abc", config["Auth:SessionSigningKeyBase64"]);
        Assert.Equal("Host=db", config["ConnectionStrings:Prism"]);
        Assert.Equal("cache:6379", config["ConnectionStrings:Redis"]);
        Assert.Equal("-----BEGIN-----", config["Auth:ApplicationPrivateKeyPem"]);
    }

    [Fact]
    public void A_later_secret_wins_over_an_earlier_one()
    {
        var store = new FakeStore(new Dictionary<string, string>
        {
            ["first"] = """{"Auth:ApplicationId":"one"}""",
            ["second"] = """{"Auth:ApplicationId":"two"}""",
        });

        Assert.Equal("two", Load(store, "first", "second")["Auth:ApplicationId"]);
    }

    [Fact]
    public void Numbers_and_booleans_are_kept_as_text()
    {
        var store = new FakeStore(new Dictionary<string, string> { ["s"] = """{"Auth:SessionLifetimeMinutes":120,"Auth:AllowIdpInitiated":false}""" });

        IConfiguration config = Load(store, "s");

        Assert.Equal("120", config["Auth:SessionLifetimeMinutes"]);
        Assert.Equal("false", config["Auth:AllowIdpInitiated"]);
    }

    [Theory]
    [InlineData("""{"ForwardedHeaders:KnownProxies:0":"10.0.0.1"}""")]
    [InlineData("""{"Aws:SecretsManager:SecretIds:0":"evil"}""")]
    [InlineData("""{"Logging:LogLevel:Default":"None"}""")]
    [InlineData("""{"AllowedHosts":"*"}""")]
    public void A_key_outside_the_allowed_sections_is_refused(string secret)
    {
        var store = new FakeStore(new Dictionary<string, string> { ["s"] = secret });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(store, "s"));

        Assert.Contains("allowed sections", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all SECRET-VALUE-123")]
    [InlineData("""["SECRET-VALUE-123"]""")]
    [InlineData("\"SECRET-VALUE-123\"")]
    public void A_secret_that_is_not_a_json_object_is_refused_without_repeating_its_content(string secret)
    {
        var store = new FakeStore(new Dictionary<string, string> { ["s"] = secret });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(store, "s"));

        Assert.DoesNotContain("SECRET-VALUE-123", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_secret_names_the_secret_id_and_nothing_else()
    {
        var store = new FakeStore([]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(store, "prism/stage/app"));

        Assert.Contains("prism/stage/app", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversized_secret_and_one_with_too_many_keys_are_refused()
    {
        string huge = "{\"Auth:K\":\"" + new string('x', SecretsManagerConfigurationProvider.MaxSecretLength) + "\"}";
        string manyKeys = "{" + string.Join(",", Enumerable.Range(0, SecretsManagerConfigurationProvider.MaxKeys + 1).Select(i => $"\"Auth:K{i}\":\"v\"")) + "}";
        var store = new FakeStore(new Dictionary<string, string> { ["huge"] = huge, ["many"] = manyKeys });

        Assert.Throws<InvalidOperationException>(() => Load(store, "huge"));
        Assert.Throws<InvalidOperationException>(() => Load(store, "many"));
    }

    [Fact]
    public void Only_the_first_ten_secret_ids_are_read()
    {
        var secrets = Enumerable.Range(0, 15).ToDictionary(i => $"s{i}", _ => """{"Auth:X":"1"}""");
        var store = new FakeStore(secrets);

        Load(store, [.. secrets.Keys]);

        Assert.Equal(SecretsManagerConfigurationProvider.MaxSecrets, store.Asked.Count);
    }

    [Fact]
    public void Nothing_is_read_and_aws_is_not_contacted_when_no_secret_ids_are_configured()
    {
        bool contacted = false;
        IConfigurationBuilder builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:ApplicationId"] = "local" });

        IConfigurationRoot config = builder.AddPrismSecretsManager(() =>
        {
            contacted = true;
            return new FakeStore([]);
        }).Build();

        Assert.False(contacted);
        Assert.Equal("local", config["Auth:ApplicationId"]);
    }

    [Fact]
    public void Configured_secret_ids_are_read_and_override_the_settings_files()
    {
        var store = new FakeStore(new Dictionary<string, string> { ["prism/stage/app"] = """{"Auth:ApplicationId":"from-secret"}""" });
        IConfigurationBuilder builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Aws:SecretsManager:SecretIds:0"] = "prism/stage/app",
            ["Auth:ApplicationId"] = "from-file",
        });

        IConfigurationRoot config = builder.AddPrismSecretsManager(() => store).Build();

        Assert.Equal("from-secret", config["Auth:ApplicationId"]);
    }
}
