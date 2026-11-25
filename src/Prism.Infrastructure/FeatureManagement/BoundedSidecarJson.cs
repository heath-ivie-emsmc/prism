using System.Text.Json;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>
/// Reads a sidecar response with a size cap (AGENTS.md rule 4). The sidecar is local and trusted, but a profile can grow, and an unbounded read of
/// an operator-edited document has no business being able to exhaust the process. A document over the cap fails the load, and the last good state stays.
/// </summary>
internal static class BoundedSidecarJson
{
    internal const int MaxBytes = 1024 * 1024;

    internal static async Task<JsonDocument> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new InvalidOperationException($"The App Config sidecar response is larger than {MaxBytes} bytes.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }
}
