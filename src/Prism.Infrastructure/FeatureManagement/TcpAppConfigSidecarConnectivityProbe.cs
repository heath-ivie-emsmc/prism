using System.Net.Sockets;


using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class TcpAppConfigSidecarConnectivityProbe : IAppConfigSidecarConnectivityProbe
{
    public async Task<bool> IsReachableAsync(FeatureManagementOptions options, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        int port = SidecarPortWait.ResolvePort(uri);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, port, cancellationToken).ConfigureAwait(false);
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
