using System.Net;
using Microsoft.Extensions.Options;
using Prism.Api;
using Prism.Infrastructure.Auth;
using Prism.Testing.Common;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Persistence;

public sealed class RedisReplayStoreTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task The_first_use_of_a_token_id_is_allowed_and_a_second_is_a_replay()
    {
        using var store = new RedisLoginTokenReplayStore(redis.ConnectionString);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.True(await store.TryRememberAsync("token-first", Window, ct));
        Assert.False(await store.TryRememberAsync("token-first", Window, ct));
        Assert.True(await store.TryRememberAsync("token-other", Window, ct));
    }

    [Fact]
    public async Task A_rotated_connection_string_is_used_by_the_next_call()
    {
        // The same container, but a different Redis database, stands in for rotated credentials pointing at the same store.
        string current = redis.ConnectionString + ",defaultDatabase=0";
        using var store = new RedisLoginTokenReplayStore(() => current);
        CancellationToken ct = TestContext.Current.CancellationToken;
        string tokenId = "token-rotated-" + Guid.NewGuid().ToString("N");

        Assert.True(await store.TryRememberAsync(tokenId, Window, ct));
        Assert.False(await store.TryRememberAsync(tokenId, Window, ct));

        current = redis.ConnectionString + ",defaultDatabase=1";
        Assert.True(await store.TryRememberAsync(tokenId, Window, ct));
        Assert.False(await store.TryRememberAsync(tokenId, Window, ct));

        current = redis.ConnectionString + ",defaultDatabase=0";
        Assert.False(await store.TryRememberAsync(tokenId, Window, ct));
    }

    [Fact]
    public async Task A_rotated_connection_string_that_goes_missing_fails_the_call_instead_of_skipping_the_check()
    {
        string current = redis.ConnectionString;
        using var store = new RedisLoginTokenReplayStore(() => current);
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.True(await store.TryRememberAsync("token-missing-" + Guid.NewGuid().ToString("N"), Window, ct));

        current = "";

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.TryRememberAsync("token-after-" + Guid.NewGuid().ToString("N"), Window, ct));
    }

    [Fact]
    public async Task Two_instances_of_the_app_share_the_memory()
    {
        using var first = new RedisLoginTokenReplayStore(redis.ConnectionString);
        using var second = new RedisLoginTokenReplayStore(redis.ConnectionString);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.True(await first.TryRememberAsync("token-shared", Window, ct));
        Assert.False(await second.TryRememberAsync("token-shared", Window, ct));
    }

    [Fact]
    public async Task A_token_id_can_be_used_again_once_its_window_has_passed()
    {
        using var store = new RedisLoginTokenReplayStore(redis.ConnectionString);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.True(await store.TryRememberAsync("token-expiring", TimeSpan.FromMilliseconds(300), ct));
        await Task.Delay(TimeSpan.FromMilliseconds(900), ct);

        Assert.True(await store.TryRememberAsync("token-expiring", Window, ct));
    }

    [Theory]
    [InlineData("a\r\nFLUSHALL\r\n")]
    [InlineData("*3\r\n$3\r\nDEL\r\n$1\r\nx\r\n")]
    [InlineData("\u0000\u0001 spaces and \"quotes\"")]
    public async Task Hostile_token_ids_are_just_keys(string tokenId)
    {
        using var store = new RedisLoginTokenReplayStore(redis.ConnectionString);
        CancellationToken ct = TestContext.Current.CancellationToken;
        // The container is shared by every test, so each case needs ids no other case has used.
        string survivor = "survivor-" + Guid.NewGuid().ToString("N");
        string hostile = tokenId + Guid.NewGuid().ToString("N");
        Assert.True(await store.TryRememberAsync(survivor, Window, ct));

        Assert.True(await store.TryRememberAsync(hostile, Window, ct));
        Assert.False(await store.TryRememberAsync(hostile, Window, ct));
        Assert.False(await store.TryRememberAsync(survivor, Window, ct));
    }

    [Fact]
    public async Task An_unreachable_redis_fails_the_call_instead_of_letting_the_token_through()
    {
        using var store = new RedisLoginTokenReplayStore("127.0.0.1:1,connectTimeout=500,syncTimeout=500,asyncTimeout=500");

        await Assert.ThrowsAnyAsync<Exception>(async () => await store.TryRememberAsync("token-x", Window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_host_configured_with_redis_refuses_a_replayed_login_token_and_stores_it_in_redis()
    {
        using var factory = new AuthTestFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redis.ConnectionString });
        using CookieClient client = factory.NewClient();
        string token = factory.Portal.Mint(DateTimeOffset.UtcNow, new LoginTokenSpec { TokenId = "redis-flow-1" });

        async Task<string> CallbackAsync()
        {
            using HttpResponseMessage start = await client.GetAsync("/api/auth/sso/start");
            string state = System.Web.HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
            using HttpResponseMessage response = await client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(token)}&state={state}");
            return response.Headers.Location!.OriginalString;
        }

        Assert.Equal("/", await CallbackAsync());
        Assert.Equal("/login?error=sso_failed", await CallbackAsync());
    }

    [Theory]
    [InlineData("Production", "", false)]
    [InlineData("Staging", "  ", false)]
    [InlineData("QA", "redis.example.test:6380,ssl=true", true)]
    [InlineData("Development", "", true)]
    [InlineData("Testing", "", true)]
    public void Deployed_environments_must_configure_redis(string environment, string connectionString, bool accepted)
    {
        ValidateOptionsResult result = new DeployedRedisOptionsValidator(new HardeningTests.FakeEnvironment(environment))
            .Validate(null, new PrismRedisOptions { ConnectionString = connectionString });

        Assert.Equal(accepted, result.Succeeded);
    }
}
