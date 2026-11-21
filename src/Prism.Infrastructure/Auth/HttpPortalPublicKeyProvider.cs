using System.Text.Json;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>Downloads the portal's public key. The response is either the PEM itself or JSON that contains it.</summary>
public sealed class HttpPortalPublicKeyProvider(
    IHttpClientFactory httpClients,
    IOptions<PrismAuthOptions> options,
    TimeProvider timeProvider) : IPortalPublicKeyProvider, IDisposable
{
    public const string HttpClientName = "portal-public-key";

    private const string PemMarker = "-----BEGIN";

    private readonly SemaphoreSlim gate = new(1, 1);
    private string? cachedPem;
    private DateTimeOffset cachedUntil;

    public async Task<string> GetPublicKeyPemAsync(CancellationToken cancellationToken)
    {
        PrismAuthOptions settings = options.Value;
        if (TryGetCached(out string? cached))
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            string pem = await DownloadAsync(settings, cancellationToken);
            if (settings.LoginTokenPublicKeyCacheSeconds > 0)
            {
                cachedPem = pem;
                cachedUntil = timeProvider.GetUtcNow().AddSeconds(settings.LoginTokenPublicKeyCacheSeconds);
            }

            return pem;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private bool TryGetCached(out string pem)
    {
        pem = cachedPem ?? "";
        return cachedPem is not null && timeProvider.GetUtcNow() < cachedUntil;
    }

    private async Task<string> DownloadAsync(PrismAuthOptions settings, CancellationToken cancellationToken)
    {
        Uri url = PortalUrls.RequireHttpsOrLoopback(settings.LoginTokenPublicKeyUrl, nameof(settings.LoginTokenPublicKeyUrl));

        using HttpClient http = httpClients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        return ExtractPem(body) ?? throw new InvalidOperationException("The portal did not return a public key.");
    }

    private static string? ExtractPem(string body)
    {
        string trimmed = body.Trim();
        if (trimmed.StartsWith(PemMarker, StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(trimmed);
            return FindPem(json.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindPem(JsonElement element, int depth)
    {
        if (depth > 4)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                string? text = element.GetString();
                return text is not null && text.Contains(PemMarker, StringComparison.Ordinal) ? text : null;
            case JsonValueKind.Object:
                return element.EnumerateObject().Select(p => FindPem(p.Value, depth + 1)).FirstOrDefault(p => p is not null);
            default:
                return null;
        }
    }
}
