using System.Text;
using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Prism.Tests.Configuration;

public sealed class AwsSecretPayloadFlattenerTests
{
    private static AwsSecretsLoaderEntry Entry(
        AwsSecretFormat format = AwsSecretFormat.Json,
        string? keyPrefix = null,
        string? targetKey = null,
        Dictionary<string, string>? keys = null) => new()
        {
            SecretId = "test/secret",
            Format = format,
            KeyPrefix = keyPrefix,
            TargetKey = targetKey,
            Keys = keys ?? [],
        };

    [Fact]
    public void Flatten_Json_DirectMode_FlattensTopLevelScalars()
    {
        const string json = """{"Demo":{"SharedRedisConnectionString":"redis://x"}}""";

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(Entry(), json);

        Assert.Contains(result, p => p.ProducedKey == "Demo:SharedRedisConnectionString" && p.Value == "redis://x");
    }

    [Fact]
    public void Flatten_Json_WithKeyPrefix_PrependsPrefixToEveryFlattenedKey()
    {
        const string json = """{"host":"service.example.com","port":22}""";
        AwsSecretsLoaderEntry entry = Entry(keyPrefix: "Example:Service");

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(entry, json);

        Assert.Contains(result, p => p.ProducedKey == "Example:Service:host" && p.SourceProperty == "host");
        Assert.Contains(result, p => p.ProducedKey == "Example:Service:port" && p.Value == "22");
    }

    [Fact]
    public void Flatten_Json_WithKeysMapping_FansOutOnePropertyToMultipleKeys()
    {
        const string json = """{"private":"-----BEGIN PRIVATE KEY-----..."}""";
        var keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Demo:EmSmart2:JwtPrivateKeyPem"] = "private",
            ["Demo:Other:SamePrivateKeyPem"] = "private",
        };
        AwsSecretsLoaderEntry entry = Entry(keys: keys);

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(entry, json);

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal("private", p.SourceProperty));
        Assert.Contains(result, p => p.ProducedKey == "Demo:EmSmart2:JwtPrivateKeyPem");
        Assert.Contains(result, p => p.ProducedKey == "Demo:Other:SamePrivateKeyPem");
    }

    [Fact]
    public void Flatten_Json_KeysMapping_MissingProperty_Throws()
    {
        const string json = """{"other":"x"}""";
        var keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["Config:Key"] = "missing" };
        AwsSecretsLoaderEntry entry = Entry(keys: keys);

        AwsSecretsLoaderException ex = Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretPayloadFlattener.Flatten(entry, json));
        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flatten_Raw_ReturnsWholeSecretStringAtTargetKey()
    {
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.Raw, targetKey: "PhpForwardTest:Forward:HmacSecret");

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(entry, "raw-secret-value");

        AwsSecretFlattenedProperty property = Assert.Single(result);
        Assert.Equal("PhpForwardTest:Forward:HmacSecret", property.ProducedKey);
        Assert.Equal("raw-secret-value", property.Value);
    }

    [Fact]
    public void Flatten_Raw_WithoutTargetKey_Throws()
    {
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.Raw);

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretPayloadFlattener.Flatten(entry, "value"));
    }

    [Fact]
    public void Flatten_RdsPostgres_DefaultsTargetKeyToConnectionStringsWriter()
    {
        const string json = """{"hostname":"db.example.com","database":"nemsis","username":"u","password":"p"}""";
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.RdsPostgres);

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(entry, json);

        AwsSecretFlattenedProperty property = Assert.Single(result);
        Assert.Equal("ConnectionStrings:Writer", property.ProducedKey);
        Assert.Contains("Host=db.example.com", property.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Flatten_RdsPostgres_InvalidJson_Throws()
    {
        AwsSecretsLoaderEntry entry = Entry(format: AwsSecretFormat.RdsPostgres);

        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretPayloadFlattener.Flatten(entry, """{"not":"rds-shaped"}"""));
    }

    [Fact]
    public void Flatten_Json_MalformedJson_Throws()
    {
        Assert.Throws<AwsSecretsLoaderException>(() => AwsSecretPayloadFlattener.Flatten(Entry(), "{not json"));
    }

    [Fact]
    public void Flatten_Json_NestedObjectsAndArrays_ProducesColonAndIndexPaths()
    {
        const string json = """
            {
              "WebApi": {
                "InternalSubmit": {
                  "Clients": [
                    { "SigningKeys": ["key-a", "key-b"] }
                  ]
                }
              }
            }
            """;

        IReadOnlyList<AwsSecretFlattenedProperty> result = AwsSecretPayloadFlattener.Flatten(Entry(), json);

        Assert.Contains(result, p => p.ProducedKey == "WebApi:InternalSubmit:Clients:0:SigningKeys:0" && p.Value == "key-a");
        Assert.Contains(result, p => p.ProducedKey == "WebApi:InternalSubmit:Clients:0:SigningKeys:1" && p.Value == "key-b");
    }

    [Fact]
    public void Flatten_Json_EquivalentToAddJsonStream_OnNestedFixture()
    {
        const string json = """
            {
              "Demo": {
                "SharedRedisConnectionString": "redis://x",
                "Nested": { "A": 1, "B": true, "C": null }
              },
              "WebApi": {
                "InternalSubmit": {
                  "Clients": [
                    { "ClientId": "abc", "SigningKeys": ["k1", "k2"] },
                    { "ClientId": "def", "SigningKeys": ["k3"] }
                  ]
                }
              }
            }
            """;

        IReadOnlyList<AwsSecretFlattenedProperty> flattened = AwsSecretPayloadFlattener.Flatten(Entry(), json);
        var actual = flattened.ToDictionary(p => p.ProducedKey, p => p.Value, StringComparer.OrdinalIgnoreCase);

        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        IConfigurationRoot expectedConfig = builder.Build();
        Dictionary<string, string?> expected = expectedConfig.AsEnumerable()
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // Our flattener omits null leaves, while AddJsonStream's provider still exposes them as null.
        // Compare only the keys that carry a value, then confirm the null key exists in both with a null value.
        foreach ((var key, var value) in expected)
        {
            Assert.True(actual.TryGetValue(key, out var actualValue), $"missing key '{key}'");
            Assert.Equal(value, actualValue);
        }

        // +1: the explicit null leaf. AddJsonStream records Demo:Nested:C with a null value, which the
        // not-null filter above drops from `expected`, but the flattener still emits it so that a secret
        // whose property is explicitly null clears the key rather than leaving a stale value behind.
        Assert.Equal(expected.Count + 1, actual.Count);
        Assert.True(actual.ContainsKey("Demo:Nested:C"));
        Assert.Null(actual["Demo:Nested:C"]);
    }
}
