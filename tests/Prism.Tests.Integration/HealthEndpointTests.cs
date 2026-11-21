using System.Net;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Theory]
    [InlineData("/up")]
    [InlineData("/ready")]
    public async Task Health_endpoints_are_anonymous_and_ok(string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_is_503_without_detail_when_the_database_is_down()
    {
        factory.Database.Available = false;
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/ready", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"unavailable"}""", body);
    }

    [Fact]
    public async Task Up_stays_ok_when_the_database_is_down()
    {
        factory.Database.Available = false;
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/up", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
