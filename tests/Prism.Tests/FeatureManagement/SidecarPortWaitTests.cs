using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class SidecarPortWaitTests
{
    [Fact]
    public void TryGetSidecarEndpoint_ParsesExplicitPort()
    {
        Assert.True(SidecarPortWait.TryGetSidecarEndpoint("http://127.0.0.1:2772", out string host, out int port));
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(2772, port);
    }

    [Fact]
    public async Task WaitUntilListeningAsync_ReturnsWhenPortOpens()
    {
        using var server = new DelayedTcpListener(TimeSpan.FromMilliseconds(300));
        var options = new FeatureManagementOptions
        {
            BaseUrl = server.BaseUrl,
            StartupMaxWait = TimeSpan.FromSeconds(5),
        };

        var stopwatch = Stopwatch.StartNew();
        await SidecarPortWait.WaitUntilListeningAsync(options, stopwatch, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250));
    }

    private sealed class DelayedTcpListener : IDisposable
    {
        private readonly TcpListener _listener;

        public string BaseUrl { get; }

        public DelayedTcpListener(TimeSpan listenDelay)
        {
            int port = GetFreeTcpPort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new TcpListener(IPAddress.Loopback, port);
            _ = StartAfterDelayAsync(listenDelay);
        }

        private async Task StartAfterDelayAsync(TimeSpan listenDelay)
        {
            await Task.Delay(listenDelay).ConfigureAwait(false);
            _listener.Start();
        }

        public void Dispose()
        {
            if (_listener.Server.IsBound)
            {
                _listener.Stop();
            }
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
