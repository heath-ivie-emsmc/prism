using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prism.Infrastructure.FeatureManagement;
using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class AppConfigSidecarHostedServiceTests
{
    private static FeatureManagementOptions MakeOptions(bool enabled = true) => new()
    {
        Enabled = enabled,
        BaseUrl = "http://localhost:2772",
        Application = "test-app",
        Environment = "dev",
        FeatureConfiguration = "feature-flags",
        AppSettingsConfiguration = "appsettings",
        PollInterval = TimeSpan.FromSeconds(1),
        StartupMaxWait = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
        RequireNonEmptyFeatureConfig = false,
        EnableDynamicAppSettings = false,
    };

    private static (AppConfigSidecarHostedService Service, SidecarState State, FeatureManagementBootstrapState Bootstrap, SidecarConfigurationProvider Provider, FakeApplicationLifetime Lifetime, SidecarPortStub PortStub)
        CreateService(
            HttpMessageHandler handler,
            FeatureManagementOptions? options = null)
    {
        var portStub = new SidecarPortStub();
        FeatureManagementOptions opts = options ?? MakeOptions();
        opts.BaseUrl = portStub.BaseUrl;
        var mockOptions = new TestOptionsMonitor<FeatureManagementOptions>(opts);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var sidecarClient = new AppConfigSidecarClient(httpClient, mockOptions, TimeProvider.System);
        var state = new SidecarState();
        var provider = new SidecarConfigurationProvider();
        provider.Load();
        var bootstrapState = new FeatureManagementBootstrapState();
        NullLogger<AppConfigSidecarHostedService> logger = NullLogger<AppConfigSidecarHostedService>.Instance;
        var lifetime = new FakeApplicationLifetime();

        var service = new AppConfigSidecarHostedService(
            sidecarClient, state, provider, mockOptions, logger, bootstrapState, lifetime);

        return (service, state, bootstrapState, provider, lifetime, portStub);
    }

    private static async Task WaitForBootstrapAsync(
        FeatureManagementBootstrapState bootstrap,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!bootstrap.IsBootstrapComplete)
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException("Timed out waiting for AppConfig sidecar bootstrap to complete.");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static async Task WaitForSidecarPollCountAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(timeoutMessage);
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    /// <summary>
    /// Polls <paramref name="lifetime"/>'s recorded invocations instead of a fixed <c>Task.Delay</c> before
    /// asserting <see cref="IHostApplicationLifetime.StopApplication"/> was called -- a bare delay races the
    /// hosted service's internal <c>StartupMaxWait</c> timeout and can flake under CPU contention (e.g. when
    /// running alongside other suites in <c>test-local-fast.sh</c>).
    /// </summary>
    private static async Task WaitForStopApplicationAsync(
        FakeApplicationLifetime lifetime,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (lifetime.StopCount == 0)
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException("Timed out waiting for AppConfig sidecar to call StopApplication().");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_CompletesWithoutFetching()
    {
        using var handler = new FakeHandler("{}");
        (AppConfigSidecarHostedService? service, SidecarState? state, FeatureManagementBootstrapState _, SidecarConfigurationProvider _, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, MakeOptions(enabled: false));
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);

            Assert.Empty(state.GetSnapshot().Features);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_FetchesFeaturesAndMarksBootstrap()
    {
        string json = """{"Feature1":{"enabled":true}}""";
        using var handler = new FakeHandler(json);
        (AppConfigSidecarHostedService? service, SidecarState? state, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(bootstrap.IsBootstrapComplete);
            Assert.Null(bootstrap.BootstrapFailure);
            Assert.True(state.TryGetFeature("Feature1", out FeatureValue? feature));
            Assert.True(feature!.Enabled);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_WithDynamicAppSettings_FetchesAppSettings()
    {
        FeatureManagementOptions options = MakeOptions();
        options.EnableDynamicAppSettings = true;
        using var handler = new FakeHandler("""{"Key":"Value"}""");
        (AppConfigSidecarHostedService? service, SidecarState _, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider? provider, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(bootstrap.IsBootstrapComplete);
            Assert.Null(bootstrap.BootstrapFailure);
            Assert.True(provider.TryGet("Key", out string? val));
            Assert.Equal("Value", val);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_FailOpen_ContinuesOnError()
    {
        FeatureManagementOptions options = MakeOptions();
        options.StartupFailureMode = StartupFailureMode.FailOpen;
        options.StartupMaxWait = TimeSpan.FromMilliseconds(500);
        using var handler = new FakeHandler(statusCode: HttpStatusCode.InternalServerError);
        (AppConfigSidecarHostedService? service, SidecarState _, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime? lifetime, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(bootstrap.IsBootstrapComplete);
            Assert.Null(bootstrap.BootstrapFailure);
            Assert.Equal(0, lifetime.StopCount);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_FailClosed_StopsApplicationOnTimeout()
    {
        FeatureManagementOptions options = MakeOptions();
        options.StartupFailureMode = StartupFailureMode.FailClosed;
        options.StartupMaxWait = TimeSpan.FromMilliseconds(500);
        using var handler = new FakeHandler(statusCode: HttpStatusCode.InternalServerError);
        (AppConfigSidecarHostedService? service, SidecarState _, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime? lifetime, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForStopApplicationAsync(
                lifetime,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            Assert.False(bootstrap.IsBootstrapComplete);
            Assert.IsType<TimeoutException>(bootstrap.BootstrapFailure);
            Assert.Equal(1, lifetime.StopCount);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_RequireNonEmpty_EmptyPayload_Throws()
    {
        FeatureManagementOptions options = MakeOptions();
        options.RequireNonEmptyFeatureConfig = true;
        using var handler = new FakeHandler("{}");
        (AppConfigSidecarHostedService? service, SidecarState _, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime? lifetime, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForStopApplicationAsync(
                lifetime,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            Assert.False(bootstrap.IsBootstrapComplete);
            InvalidOperationException failure = Assert.IsType<InvalidOperationException>(bootstrap.BootstrapFailure);
            Assert.Equal("The feature configuration payload was empty during startup.", failure.Message);
            Assert.Equal(1, lifetime.StopCount);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_CancellationDuringBootstrap_DoesNotMarkBootstrapComplete()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new SlowHandler(cts);
        FeatureManagementOptions options = MakeOptions();
        options.StartupMaxWait = TimeSpan.FromSeconds(30);
        (AppConfigSidecarHostedService? service, SidecarState _, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

            Assert.False(bootstrap.IsBootstrapComplete);
            Assert.Null(bootstrap.BootstrapFailure);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_RetriesUntilSidecarResponds()
    {
        using var handler = new RetryThenSuccessHandler();
        FeatureManagementOptions options = MakeOptions();
        options.StartupMaxWait = TimeSpan.FromSeconds(5);
        (AppConfigSidecarHostedService? service, SidecarState? state, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider _, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.True(bootstrap.IsBootstrapComplete);
            Assert.Null(bootstrap.BootstrapFailure);
            Assert.True(state.TryGetFeature("Feature1", out FeatureValue? feature));
            Assert.True(feature!.Enabled);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PollsAndRefreshesFeaturesAndAppSettings()
    {
        FeatureManagementOptions options = MakeOptions();
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.EnableDynamicAppSettings = true;
        using var handler = new RefreshTrackingHandler();
        (AppConfigSidecarHostedService? service, SidecarState? state, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider? provider, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await WaitForSidecarPollCountAsync(
                () => handler.FeatureRefreshCount >= 2
                    && handler.AppSettingsRefreshCount >= 2
                    && state.TryGetFeature("RefreshedFeature", out _)
                    && provider.TryGet("RefreshedKey", out string? refreshedValue)
                    && refreshedValue == "RefreshedValue",
                TimeSpan.FromSeconds(5),
                "Timed out waiting for AppConfig sidecar poll refresh.",
                TestContext.Current.CancellationToken);
            await service.StopAsync(TestContext.Current.CancellationToken);

            Assert.True(handler.FeatureRefreshCount >= 2);
            Assert.True(handler.AppSettingsRefreshCount >= 2);
            Assert.True(state.TryGetFeature("RefreshedFeature", out _));
            Assert.Equal("RefreshedValue", provider.TryGet("RefreshedKey", out string? value) ? value : null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RefreshFailures_KeepLastKnownState()
    {
        FeatureManagementOptions options = MakeOptions();
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.EnableDynamicAppSettings = true;
        using var handler = new RefreshFailureHandler();
        (AppConfigSidecarHostedService? service, SidecarState? state, FeatureManagementBootstrapState? bootstrap, SidecarConfigurationProvider? provider, FakeApplicationLifetime _, SidecarPortStub? portStub) = CreateService(handler, options);
        using (portStub)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            await WaitForBootstrapAsync(
                bootstrap,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await WaitForSidecarPollCountAsync(
                () => handler.FeatureRefreshCount >= 2 && handler.AppSettingsRefreshCount >= 2,
                TimeSpan.FromSeconds(5),
                "Timed out waiting for AppConfig sidecar poll attempt after bootstrap.",
                TestContext.Current.CancellationToken);
            await service.StopAsync(TestContext.Current.CancellationToken);

            Assert.True(state.TryGetFeature("BootstrapFeature", out _));
            Assert.True(provider.TryGet("BootstrapKey", out string? value));
            Assert.Equal("BootstrapValue", value);
        }
    }

    private sealed class SidecarPortStub : IDisposable
    {
        private readonly TcpListener _listener;

        public string BaseUrl { get; }

        public SidecarPortStub()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource _cts;

        public SlowHandler(CancellationTokenSource cts) => _cts = cts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class RetryThenSuccessHandler : HttpMessageHandler
    {
        private int _attempt;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempt++;
            if (_attempt == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"Feature1":{"enabled":true}}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class RefreshTrackingHandler : HttpMessageHandler
    {
        public int FeatureRefreshCount { get; private set; }

        public int AppSettingsRefreshCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/configurations/feature-flags", StringComparison.Ordinal))
            {
                FeatureRefreshCount++;
                string json = FeatureRefreshCount == 1
                    ? """{"BootstrapFeature":{"enabled":true}}"""
                    : """{"RefreshedFeature":{"enabled":true}}""";
                return Ok(json);
            }

            if (path.Contains("/configurations/appsettings", StringComparison.Ordinal))
            {
                AppSettingsRefreshCount++;
                string json = AppSettingsRefreshCount == 1
                    ? """{"BootstrapKey":"BootstrapValue"}"""
                    : """{"RefreshedKey":"RefreshedValue"}""";
                return Ok(json);
            }

            return Ok("{}");
        }

        private static Task<HttpResponseMessage> Ok(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class RefreshFailureHandler : HttpMessageHandler
    {
        public int FeatureRefreshCount { get; private set; }

        public int AppSettingsRefreshCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/configurations/feature-flags", StringComparison.Ordinal))
            {
                FeatureRefreshCount++;
                if (FeatureRefreshCount == 1)
                {
                    return Ok("""{"BootstrapFeature":{"enabled":true}}""");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            if (path.Contains("/configurations/appsettings", StringComparison.Ordinal))
            {
                AppSettingsRefreshCount++;
                if (AppSettingsRefreshCount == 1)
                {
                    return Ok("""{"BootstrapKey":"BootstrapValue"}""");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Ok("{}");
        }

        private static Task<HttpResponseMessage> Ok(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public FakeHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}

/// <summary>Counts how often the host was asked to stop, instead of a mocking library.</summary>
internal sealed class FakeApplicationLifetime : IHostApplicationLifetime
{
    private int stopCount;

    public int StopCount => Volatile.Read(ref stopCount);

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => Interlocked.Increment(ref stopCount);
}
