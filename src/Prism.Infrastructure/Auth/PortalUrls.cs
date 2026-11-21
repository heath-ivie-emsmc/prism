namespace Prism.Infrastructure.Auth;

internal static class PortalUrls
{
    /// <summary>Portal calls carry credentials or trust decisions, so they use TLS. Loopback is allowed for local development.</summary>
    public static Uri RequireHttpsOrLoopback(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute https URL.");
        }

        return uri;
    }

    /// <summary>Returns a base URI that ends in a slash, so relative paths are appended and not substituted.</summary>
    public static Uri AsDirectory(Uri uri) =>
        uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = string.Concat(uri.AbsolutePath, '/') }.Uri;
}
