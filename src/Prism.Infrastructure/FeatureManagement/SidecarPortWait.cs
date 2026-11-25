using System.Diagnostics;
using System.Net.Sockets;


using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>
/// Waits until the AppConfig agent accepts TCP connections before the first HTTP call.
/// Avoids connection-refused noise when the app container starts in parallel with the sidecar.
/// </summary>
public static class SidecarPortWait
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static Task WaitUntilListeningAsync(
        FeatureManagementOptions options,
        Stopwatch budgetStopwatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budgetStopwatch);
        return WaitUntilListeningCoreAsync(options, budgetStopwatch, cancellationToken);
    }

    private static async Task WaitUntilListeningCoreAsync(
        FeatureManagementOptions options,
        Stopwatch budgetStopwatch,
        CancellationToken cancellationToken)
    {
        if (!TryGetSidecarEndpoint(options.BaseUrl, out string host, out int port))
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsPortListeningAsync(host, port, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (budgetStopwatch.Elapsed >= options.StartupMaxWait)
            {
                return;
            }

            TimeSpan remaining = options.StartupMaxWait - budgetStopwatch.Elapsed;
            TimeSpan delay = remaining < PollInterval ? remaining : PollInterval;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool TryGetSidecarEndpoint(string baseUrl, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        host = uri.Host;
        port = ResolvePort(uri);
        return true;
    }

    public static int ResolvePort(Uri uri)
    {
        if (!uri.IsDefaultPort)
        {
            return uri.Port;
        }

        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return 443;
        }

        return 80;
    }

    private static async Task<bool> IsPortListeningAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
