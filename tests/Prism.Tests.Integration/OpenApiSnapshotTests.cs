using System.Text.Json;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration;

/// <summary>
/// The generated OpenAPI document is committed, and any change to it fails this test until a person looks at it and updates the file.
/// That makes a breaking change to the public API a deliberate act. To accept a change:
/// <c>PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests.Integration --filter OpenApiSnapshotTests</c>, then review the diff.
/// </summary>
public sealed class OpenApiSnapshotTests : IDisposable
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task The_generated_document_matches_the_committed_snapshot()
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string current = JsonSerializer.Serialize(JsonElement.Parse(raw), Pretty) + "\n";
        string path = Path.Combine(FindRoot(), "tests", "Prism.Tests.Integration", "Snapshots", "openapi.v1.json");

        if (Environment.GetEnvironmentVariable("PRISM_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(path, current, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), "No snapshot yet. Create it with PRISM_UPDATE_SNAPSHOTS=1.");
        string committed = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(committed.ReplaceLineEndings("\n"), current);
    }

    [Fact]
    public async Task Every_documented_operation_has_a_summary_and_a_description()
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var missing = new List<string>();
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("summary", out _) || !operation.Value.TryGetProperty("description", out _))
                {
                    missing.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/docs")]
    public async Task The_api_description_and_its_reference_page_refuse_an_anonymous_caller(string path)
    {
        using CookieClient anonymous = factory.NewClient();

        using HttpResponseMessage response = await anonymous.GetAsync(path);

        Assert.True(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden, $"{path} answered {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_signed_in_user_can_open_the_reference_page_and_it_is_not_the_spa()
    {
        using CookieClient client = factory.NewSignedInClient();

        using HttpResponseMessage response = await client.GetAsync("/docs/");
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Prism API", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_json_api_lives_under_v1_and_the_unversioned_path_does_not_exist()
    {
        using CookieClient client = factory.NewSignedInClient();

        using HttpResponseMessage versioned = await client.GetAsync("/api/v1/me");
        using HttpResponseMessage unversioned = await client.GetAsync("/api/me");

        Assert.Equal(System.Net.HttpStatusCode.OK, versioned.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unversioned.StatusCode);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Prism.slnx was not found above the test output folder.");
    }
}
