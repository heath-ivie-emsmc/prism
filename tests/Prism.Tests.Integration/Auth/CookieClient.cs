using System.Net.Http.Headers;

namespace Prism.Tests.Integration.Auth;

/// <summary>An HttpClient plus a hand-managed cookie jar, so tests can check Set-Cookie attributes and control what is sent back.</summary>
public sealed class CookieClient(HttpClient http) : IDisposable
{
    private readonly Dictionary<string, string> jar = [];

    public IReadOnlyDictionary<string, string> Cookies => jar;

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (jar.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}")));
        }

        foreach (KeyValuePair<string, string> header in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Absorb(response);
        return response;
    }

    public void Set(string name, string value) => jar[name] = value;

    public void Dispose() => http.Dispose();

    /// <summary>Raw Set-Cookie header for a cookie name, or null.</summary>
    public static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    public static string Value(string setCookie) => setCookie.Split(';')[0].Split('=', 2)[1];

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private void Absorb(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            return;
        }

        foreach (string raw in values)
        {
            string[] pair = raw.Split(';')[0].Split('=', 2);
            bool deleted = raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || pair[1].Length == 0;
            if (deleted)
            {
                jar.Remove(pair[0]);
            }
            else
            {
                jar[pair[0]] = pair[1];
            }
        }
    }
}
