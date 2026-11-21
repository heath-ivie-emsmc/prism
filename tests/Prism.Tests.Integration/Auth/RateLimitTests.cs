using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Prism.Api.Auth;

namespace Prism.Tests.Integration.Auth;

public sealed class RateLimitTests
{
    private static Dictionary<string, string?> Limits(int start, int callback, params (string Key, string? Value)[] more)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:RateLimitPerMinute"] = start.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Auth:CallbackRateLimitPerMinute"] = callback.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach ((string key, string? value) in more)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static async Task<List<HttpStatusCode>> HitAsync(CookieClient client, int times, string path, Func<int, IDictionary<string, string>?>? headers = null)
    {
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < times; i++)
        {
            using HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, path, headers: headers?.Invoke(i));
            codes.Add(response.StatusCode);
        }

        return codes;
    }

    [Fact]
    public async Task Starting_a_sign_in_is_limited_per_client()
    {
        using var factory = new AuthTestFactory(settings: Limits(3, 10));
        using CookieClient client = factory.NewClient();

        List<HttpStatusCode> codes = await HitAsync(client, 5, "/api/auth/sso/start");

        Assert.Equal(3, codes.Count(code => code == HttpStatusCode.Redirect));
        Assert.Equal(2, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task The_token_consuming_routes_have_their_own_tighter_budget()
    {
        using var factory = new AuthTestFactory(settings: Limits(30, 2));
        using CookieClient client = factory.NewClient();

        List<HttpStatusCode> callbacks = await HitAsync(client, 4, "/api/auth/sso/callback?loginToken=x");
        List<HttpStatusCode> starts = await HitAsync(client, 3, "/api/auth/sso/start");

        Assert.Equal(2, callbacks.Count(code => code == HttpStatusCode.TooManyRequests));
        Assert.All(starts, code => Assert.Equal(HttpStatusCode.Redirect, code));
    }

    [Fact]
    public async Task A_spoofed_forwarded_for_header_does_not_buy_a_new_budget_when_no_proxy_is_trusted()
    {
        using var factory = new AuthTestFactory(settings: Limits(3, 10));
        using CookieClient client = factory.NewClient();

        List<HttpStatusCode> codes = await HitAsync(
            client, 6, "/api/auth/sso/start", i => new Dictionary<string, string> { ["X-Forwarded-For"] = $"203.0.113.{i + 1}" });

        Assert.Equal(3, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_forwarded_client_gets_its_own_budget()
    {
        using var factory = new AuthTestFactory(
            settings: Limits(2, 10, ("ForwardedHeaders:KnownProxies:0", "10.0.0.1")),
            configureServices: services => services.AddTransient<IStartupFilter, ProxyAddressFilter>());
        using CookieClient client = factory.NewClient();

        List<HttpStatusCode> clientA = await HitAsync(client, 3, "/api/auth/sso/start", _ => new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.1" });
        List<HttpStatusCode> clientB = await HitAsync(client, 2, "/api/auth/sso/start", _ => new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.2" });

        Assert.Equal(HttpStatusCode.TooManyRequests, clientA[2]);
        Assert.All(clientB, code => Assert.Equal(HttpStatusCode.Redirect, code));
    }

    [Fact]
    public async Task A_forwarded_header_from_an_address_that_is_not_a_trusted_proxy_is_ignored()
    {
        using var factory = new AuthTestFactory(
            settings: Limits(2, 10, ("ForwardedHeaders:KnownProxies:0", "10.0.0.99")),
            configureServices: services => services.AddTransient<IStartupFilter, ProxyAddressFilter>());
        using CookieClient client = factory.NewClient();

        List<HttpStatusCode> codes = await HitAsync(
            client, 4, "/api/auth/sso/start", i => new Dictionary<string, string> { ["X-Forwarded-For"] = $"203.0.113.{i + 1}" });

        Assert.Equal(2, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("192.0.2.7", "192.0.2.7")]
    [InlineData("::ffff:192.0.2.7", "192.0.2.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "2001:db8:1:2::/64")]
    public void Clients_are_keyed_so_one_cannot_hold_many_buckets(string? address, string expected)
    {
        Assert.Equal(expected, ClientAddress.PartitionKey(address is null ? null : IPAddress.Parse(address)));
    }

    /// <summary>TestServer has no remote address. This puts a fixed one on every connection, as a load balancer would appear.</summary>
    private sealed class ProxyAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
