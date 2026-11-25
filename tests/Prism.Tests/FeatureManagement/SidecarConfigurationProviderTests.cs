using Microsoft.Extensions.Configuration;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class SidecarConfigurationProviderTests
{
    [Fact]
    public void Load_InitializesEmptyData()
    {
        var provider = new SidecarConfigurationProvider();
        provider.Load();

        Assert.False(provider.TryGet("anything", out _));
    }

    [Fact]
    public void Replace_SetsNewData()
    {
        var provider = new SidecarConfigurationProvider();
        provider.Load();

        provider.Replace(new Dictionary<string, string?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = "Value2",
        });

        Assert.True(provider.TryGet("Key1", out string? v1));
        Assert.Equal("Value1", v1);
        Assert.True(provider.TryGet("Key2", out string? v2));
        Assert.Equal("Value2", v2);
    }

    [Fact]
    public void Replace_OverwritesPreviousData()
    {
        var provider = new SidecarConfigurationProvider();
        provider.Load();

        provider.Replace(new Dictionary<string, string?> { ["OldKey"] = "OldValue" });
        provider.Replace(new Dictionary<string, string?> { ["NewKey"] = "NewValue" });

        Assert.False(provider.TryGet("OldKey", out _));
        Assert.True(provider.TryGet("NewKey", out string? value));
        Assert.Equal("NewValue", value);
    }

    [Fact]
    public void Replace_FiresReloadToken()
    {
        var provider = new SidecarConfigurationProvider();
        provider.Load();

        bool reloaded = false;
        Microsoft.Extensions.Primitives.IChangeToken token = provider.GetReloadToken();
        token.RegisterChangeCallback(_ => reloaded = true, null);

        provider.Replace(new Dictionary<string, string?> { ["Key"] = "Value" });

        Assert.True(reloaded);
    }

    [Fact]
    public void Build_ReturnsSelf()
    {
        var provider = new SidecarConfigurationProvider();
        IConfigurationProvider built = provider.Build(new ConfigurationBuilder());
        Assert.Same(provider, built);
    }
}
