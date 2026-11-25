using Prism.Infrastructure.Configuration;

namespace Prism.Tests.Configuration;

public sealed class ReloadingResourceTests
{
    [Fact]
    public void Rotation_KeepsInflightLease_AndDisposesRetiredGenerationAfterRelease()
    {
        string key = "first";
        using var owner = new ReloadingResource<Resource>(() => key, _ => new Resource());
        using var first = owner.Acquire();
        using var same = owner.Acquire();
        Assert.Same(first.Resource, same.Resource);
        key = "second";
        using var second = owner.Acquire();
        Assert.NotSame(first.Resource, second.Resource);
        Assert.False(first.Resource.Disposed);
        first.Dispose();
        Assert.False(same.Resource.Disposed);
        same.Dispose();
        Assert.True(first.Resource.Disposed);
        Assert.False(second.Resource.Disposed);
    }

    [Fact]
    public void Rotation_BoundsOutstandingGenerations_AndResumesAfterDrain()
    {
        string key = "one";
        using var owner = new ReloadingResource<Resource>(() => key, _ => new Resource());
        using var one = owner.Acquire();
        key = "two";
        using var two = owner.Acquire();
        key = "three";
        Assert.Throws<InvalidOperationException>(() => owner.Acquire());
        one.Dispose();
        using var three = owner.Acquire();
        Assert.NotSame(two.Resource, three.Resource);
    }

    [Fact]
    public void FailedReplacement_DoesNotDisposeInflightResource_OrUseStaleCredentials()
    {
        string key = "one";
        using var owner = new ReloadingResource<Resource>(() => key, value =>
            value == "bad" ? throw new InvalidOperationException("connect failed") : new Resource());
        using var one = owner.Acquire();
        key = "bad";
        Assert.Throws<InvalidOperationException>(() => owner.Acquire());
        Assert.False(one.Resource.Disposed);
        key = "new";
        using var next = owner.Acquire();
        Assert.NotSame(one.Resource, next.Resource);
    }

    [Fact]
    public void Shutdown_RejectsNewLeases_ButLetsInflightResourceDrain()
    {
        var owner = new ReloadingResource<Resource>(() => "one", _ => new Resource());
        var lease = owner.Acquire();
        owner.Dispose();
        Assert.False(lease.Resource.Disposed);
        Assert.Throws<ObjectDisposedException>(() => owner.Acquire());
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, lease.Resource.DisposeCount);
    }

    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;
        public void Dispose() => DisposeCount++;
    }
}
