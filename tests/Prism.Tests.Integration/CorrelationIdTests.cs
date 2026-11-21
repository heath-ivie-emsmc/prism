using Prism.Api.Http;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration;

public sealed class CorrelationIdTests : IDisposable
{
    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<string> IdForAsync(string? sent)
    {
        using CookieClient client = factory.NewClient();
        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, "/up", headers: sent is null ? null : new Dictionary<string, string> { [CorrelationIdMiddleware.HeaderName] = sent });
        return response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
    }

    [Fact]
    public async Task A_request_without_an_id_gets_a_fresh_one_each_time()
    {
        string first = await IdForAsync(null);
        string second = await IdForAsync(null);

        Assert.Equal(16, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task A_plain_id_from_the_caller_is_kept()
    {
        Assert.Equal("trace-42.a_b", await IdForAsync("trace-42.a_b"));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("quote\"inside")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("%0d%0aInjected: yes")]
    public async Task An_id_with_odd_characters_is_replaced(string sent)
    {
        string id = await IdForAsync(sent);

        Assert.NotEqual(sent, id);
        Assert.Equal(16, id.Length);
    }

    [Fact]
    public async Task A_too_long_id_is_replaced()
    {
        string id = await IdForAsync(new string('a', CorrelationIdMiddleware.MaxLength + 1));

        Assert.Equal(16, id.Length);
    }
}
