using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Prism.Infrastructure.FeatureManagement;
using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureManagementConfigurationBootstrapTests
{
    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_WhenDisabled_DoesNotThrow()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Enabled"] = "false",
        });

        Exception? ex = await Record.ExceptionAsync(async () => await configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_WhenEnabled_FetchesAndMergesAppSettings()
    {
        using var server = LocalJsonServer.Start("""{"MergedKey":"MergedValue"}""");
        ConfigurationManager configuration = CreateEnabledConfiguration(server.BaseUrl);

        await configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Equal("MergedValue", configuration["MergedKey"]);
    }

    // A 404 means the profile does not exist for this application/environment, which is "no overrides
    // configured" rather than a failure — every environment's identity ships in its appsettings file.
    // This must hold under the default FailClosed, and must not spend StartupMaxWait retrying first.
    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_WhenProfileDoesNotExist_ContinuesWithoutOverrides()
    {
        using var server = LocalJsonServer.Start("""{"MergedKey":"MergedValue"}""", HttpStatusCode.NotFound);
        ConfigurationManager configuration = CreateEnabledConfiguration(server.BaseUrl);
        configuration["FeatureManagement:StartupMaxWait"] = "00:00:30";

        var stopwatch = Stopwatch.StartNew();
        Exception? exception = await Record.ExceptionAsync(() => configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken));
        stopwatch.Stop();

        Assert.Null(exception);
        Assert.Null(configuration["MergedKey"]);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"A 404 is permanent and must not be retried; took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_FailOpen_OnFetchFailure_DoesNotThrow()
    {
        ConfigurationManager configuration = CreateEnabledConfiguration("http://127.0.0.1:1");
        configuration["FeatureManagement:StartupFailureMode"] = nameof(StartupFailureMode.FailOpen);
        configuration["FeatureManagement:StartupMaxWait"] = "00:00:01";
        configuration["FeatureManagement:RequestTimeout"] = "00:00:01";

        await configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Null(configuration["MergedKey"]);
    }

    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_WaitsForSidecarPortBeforeHttpFetch()
    {
        using var delayedServer = new DelayedLocalJsonServer(
            """{"MergedKey":"MergedValue"}""",
            listenDelay: TimeSpan.FromMilliseconds(400));
        ConfigurationManager configuration = CreateEnabledConfiguration(delayedServer.BaseUrl);
        configuration["FeatureManagement:StartupMaxWait"] = "00:00:05";

        var stopwatch = Stopwatch.StartNew();
        await configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal("MergedValue", configuration["MergedKey"]);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_MissingAppSettingsConfiguration_Throws()
    {
        ConfigurationManager configuration = CreateEnabledConfiguration("http://127.0.0.1:2772");
        configuration["FeatureManagement:AppSettingsConfiguration"] = "";

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken));

        Assert.Contains("AppSettingsConfiguration", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergeSidecarAppSettingsIfEnabled_MissingBaseUrl_Throws()
    {
        ConfigurationManager configuration = CreateEnabledConfiguration("http://127.0.0.1:2772");
        configuration["FeatureManagement:BaseUrl"] = "";

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => configuration.MergeSidecarAppSettingsIfEnabledAsync(TestContext.Current.CancellationToken));

        Assert.Contains("BaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConfigurationUri_EscapesSegments()
    {
        Uri uri = FeatureManagementConfigurationBootstrap.BuildConfigurationUri(
            "http://127.0.0.1:2772",
            "nemsis-soapapi",
            "dev",
            "freeform-config");

        Assert.Equal(
            "http://127.0.0.1:2772/applications/nemsis-soapapi/environments/dev/configurations/freeform-config",
            uri.ToString());
    }

    private static ConfigurationManager CreateEnabledConfiguration(string baseUrl)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Enabled"] = "true",
            ["FeatureManagement:EnableDynamicAppSettings"] = "true",
            ["FeatureManagement:BaseUrl"] = baseUrl,
            ["FeatureManagement:Application"] = "nemsis-soapapi",
            ["FeatureManagement:Environment"] = "dev",
            ["FeatureManagement:AppSettingsConfiguration"] = "appsettings",
            ["FeatureManagement:RequestTimeout"] = "00:00:05",
            ["FeatureManagement:StartupMaxWait"] = "00:00:05",
        });
        return configuration;
    }

    private sealed class LocalJsonServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _json;
        private readonly HttpStatusCode _statusCode;

        public string BaseUrl { get; }

        private LocalJsonServer(HttpListener listener, string baseUrl, string json, HttpStatusCode statusCode)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _json = json;
            _statusCode = statusCode;
            _ = Task.Run(HandleLoopAsync);
        }

        public static LocalJsonServer Start(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            int port = GetFreeTcpPort();
            var listener = new HttpListener();
            string baseUrl = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add($"{baseUrl}/");
            listener.Start();
            return new LocalJsonServer(listener, baseUrl, json, statusCode);
        }

        private async Task HandleLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                byte[] payload = Encoding.UTF8.GetBytes(_json);
                context.Response.StatusCode = (int)_statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class DelayedLocalJsonServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _startTask;
        private readonly string _json;

        public string BaseUrl { get; }

        public DelayedLocalJsonServer(string json, TimeSpan listenDelay)
        {
            _json = json;
            int port = GetFreeTcpPort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{BaseUrl}/");
            _startTask = StartListeningAfterDelayAsync(listenDelay);
            _ = Task.Run(HandleLoopAsync);
        }

        private async Task StartListeningAfterDelayAsync(TimeSpan listenDelay)
        {
            await Task.Delay(listenDelay).ConfigureAwait(false);
            _listener.Start();
        }

        private async Task HandleLoopAsync()
        {
            await _startTask.ConfigureAwait(false);

            while (_listener.IsListening)
            {
                HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                byte[] payload = Encoding.UTF8.GetBytes(_json);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
