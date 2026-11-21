using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

public sealed class PortalCompanyNamesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly TestTimeProvider clock = new(Now);
    private readonly PortalCompanyNames names;
    private int lookups;

    public PortalCompanyNamesTests() => names = new PortalCompanyNames(clock);

    private Func<CancellationToken, Task<string?>> Returns(string? name) => _ =>
    {
        lookups++;
        return Task.FromResult(name);
    };

    [Fact]
    public async Task A_name_is_looked_up_once_and_then_served_from_memory()
    {
        await names.NameAsync("12", Returns("Mercy Air"), TestContext.Current.CancellationToken);
        string? again = await names.NameAsync("12", Returns("Other"), TestContext.Current.CancellationToken);

        Assert.Equal("Mercy Air", again);
        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task A_name_is_looked_up_again_after_a_day()
    {
        await names.NameAsync("12", Returns("Mercy Air"), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromHours(25));
        string? renamed = await names.NameAsync("12", Returns("Mercy Air Medical"), TestContext.Current.CancellationToken);

        Assert.Equal("Mercy Air Medical", renamed);
        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task An_unknown_company_is_not_looked_up_again_for_five_minutes()
    {
        Assert.Null(await names.NameAsync("999", Returns(null), TestContext.Current.CancellationToken));
        Assert.Null(await names.NameAsync("999", Returns(null), TestContext.Current.CancellationToken));
        Assert.Equal(1, lookups);

        clock.Advance(TimeSpan.FromMinutes(6));
        await names.NameAsync("999", Returns(null), TestContext.Current.CancellationToken);
        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task A_failed_lookup_gives_no_name_and_is_tried_again_next_time()
    {
        string? failed = await names.NameAsync("12", _ => throw new HttpRequestException("down"), TestContext.Current.CancellationToken);
        string? retried = await names.NameAsync("12", Returns("Mercy Air"), TestContext.Current.CancellationToken);

        Assert.Null(failed);
        Assert.Equal("Mercy Air", retried);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_serving_the_old_name()
    {
        await names.NameAsync("12", Returns("Mercy Air"), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(25));

        string? stale = await names.NameAsync("12", _ => throw new HttpRequestException("down"), TestContext.Current.CancellationToken);

        Assert.Equal("Mercy Air", stale);
    }

    [Fact]
    public async Task A_caller_that_cancels_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            names.NameAsync("12", token => Task.FromCanceled<string?>(token), cancelled.Token));
    }
}
