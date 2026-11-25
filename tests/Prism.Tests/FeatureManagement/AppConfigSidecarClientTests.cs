using System.Net;
using Microsoft.Extensions.Options;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class AppConfigSidecarClientTests
{
    private static FeatureManagementOptions DefaultOptions => new()
    {
        Enabled = true,
        BaseUrl = "http://localhost:2772",
        Application = "test-app",
        Environment = "dev",
        FeatureConfiguration = "feature-flags",
        AppSettingsConfiguration = "appsettings",
    };

    private static AppConfigSidecarClient CreateClient(
        HttpMessageHandler handler,
        FeatureManagementOptions? options = null)
    {
        var mockOptions = new TestOptionsMonitor<FeatureManagementOptions>(options ?? DefaultOptions);
        var httpClient = new HttpClient(handler);
        return new AppConfigSidecarClient(httpClient, mockOptions, TimeProvider.System);
    }

    [Fact]
    public async Task FetchFeatureSnapshotAsync_ParsesFeatures()
    {
        string json = """{"WorkerProcessingEnabled":{"enabled":true},"SubmitDataEnabled":{"enabled":false}}""";
        using var handler = new FakeHandler(json);
        AppConfigSidecarClient client = CreateClient(handler);

        SidecarState.Snapshot snapshot = await client.FetchFeatureSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.Features["WorkerProcessingEnabled"].Enabled);
        Assert.False(snapshot.Features["SubmitDataEnabled"].Enabled);
        Assert.True(snapshot.RetrievedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task FetchFeatureSnapshotAsync_BuildsCorrectUrl()
    {
        using var handler = new FakeHandler("{}");
        AppConfigSidecarClient client = CreateClient(handler);

        await client.FetchFeatureSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Contains("/applications/test-app/environments/dev/configurations/feature-flags", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task FetchFeatureSnapshotAsync_TrimsTrailingSlashFromBaseUrl()
    {
        FeatureManagementOptions options = DefaultOptions;
        options.BaseUrl = "http://localhost:2772/";
        using var handler = new FakeHandler("{}");
        AppConfigSidecarClient client = CreateClient(handler, options);

        await client.FetchFeatureSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("http://localhost:2772/applications/", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task FetchFeatureSnapshotAsync_HttpFailure_Throws()
    {
        using var handler = new FakeHandler(statusCode: HttpStatusCode.InternalServerError);
        AppConfigSidecarClient client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.FetchFeatureSnapshotAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAppSettingsAsync_ParsesSettings()
    {
        string json = """{"Key1":"Value1","Nested":{"Key2":"Value2"}}""";
        using var handler = new FakeHandler(json);
        AppConfigSidecarClient client = CreateClient(handler);

        IReadOnlyDictionary<string, string?> settings = await client.FetchAppSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Value1", settings["Key1"]);
        Assert.Equal("Value2", settings["Nested:Key2"]);
    }

    [Fact]
    public async Task FetchAppSettingsAsync_EmptyConfigurationName_Throws()
    {
        FeatureManagementOptions options = DefaultOptions;
        options.AppSettingsConfiguration = "";
        using var handler = new FakeHandler("{}");
        AppConfigSidecarClient client = CreateClient(handler, options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchAppSettingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAppSettingsAsync_BuildsCorrectUrl()
    {
        using var handler = new FakeHandler("{}");
        AppConfigSidecarClient client = CreateClient(handler);

        await client.FetchAppSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Contains("/configurations/appsettings", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task FetchAppSettingsAsync_UsesApplicationAndEnvironmentInUrl()
    {
        FeatureManagementOptions options = DefaultOptions;
        options.Application = "myapp";
        options.Environment = "staging";
        using var handler = new FakeHandler("{}");
        AppConfigSidecarClient client = CreateClient(handler, options);

        await client.FetchAppSettingsAsync(TestContext.Current.CancellationToken);

        string url = handler.LastRequestUri!.ToString();
        Assert.Contains("/applications/myapp/", url);
        Assert.Contains("/environments/staging/", url);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public Uri? LastRequestUri { get; private set; }

        public FakeHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
