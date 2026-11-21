using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>The host reads <c>ConnectionStrings:Warehouse</c> and the Warehouse section, and uses the real warehouse only when a connection is set.</summary>
public sealed class WarehouseWiringTests
{
    private static AuthTestFactory Host(params (string Key, string Value)[] settings) =>
        new(settings: settings.ToDictionary(s => s.Key, s => (string?)s.Value));

    [Fact]
    public void A_connection_string_in_configuration_switches_the_host_to_the_real_warehouse()
    {
        using AuthTestFactory factory = Host(("ConnectionStrings:Warehouse", "Host=dw.invalid;Database=dw;Username=u;Password=p"));

        using HttpClient warm = factory.CreateClient();
        PrismWarehouseOptions options = factory.Services.GetRequiredService<IOptions<PrismWarehouseOptions>>().Value;

        Assert.True(options.IsConfigured);
        Assert.IsType<NpgsqlWarehouse>(factory.Services.GetRequiredService<IWarehouse>());
    }

    [Fact]
    public void Without_a_connection_string_the_host_uses_the_unavailable_warehouse()
    {
        using AuthTestFactory factory = Host();

        using HttpClient warm = factory.CreateClient();

        Assert.IsType<UnconfiguredWarehouse>(factory.Services.GetRequiredService<IWarehouse>());
    }

    [Fact]
    public void The_warehouse_section_and_the_connection_string_are_both_read()
    {
        using AuthTestFactory factory = Host(
            ("ConnectionStrings:Warehouse", "Host=dw.invalid;Database=dw;Username=u;Password=p"),
            ("Warehouse:DevelopmentProviderId", "2026"),
            ("Warehouse:MaxRows", "55"));

        using HttpClient warm = factory.CreateClient();
        PrismWarehouseOptions options = factory.Services.GetRequiredService<IOptions<PrismWarehouseOptions>>().Value;

        Assert.Equal(2026, options.DevelopmentProviderId);
        Assert.Equal(55, options.MaxRows);
        Assert.StartsWith("Host=dw.invalid", options.ConnectionString, StringComparison.Ordinal);
    }
}
