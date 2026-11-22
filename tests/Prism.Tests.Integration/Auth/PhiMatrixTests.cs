using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Prism.Application;
using Prism.Application.Auth;

namespace Prism.Tests.Integration.Auth;

/// <summary>
/// Finds every /api route whose response type carries a <see cref="PhiAttribute"/> property and requires it to be listed here with the permission it
/// is gated by. A new route that returns PHI fails this test until someone adds it, and with it the with-permission and without-permission tests.
/// </summary>
public sealed class PhiMatrixTests : IDisposable
{
    /// <summary>Route templates that return PHI, and the policy each must name. Each has endpoint tests for the with-permission and without-permission cases.</summary>
    private static readonly Dictionary<string, string> PhiRoutes = new(StringComparer.Ordinal)
    {
        ["/api/v1/providers/{providerId}/claims/attention/{checkKey}"] = Permissions.ClaimsView,
        ["/api/v1/providers/{providerId}/claims/attention/{checkKey}/export"] = Permissions.ClaimsView,
    };

    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static bool CarriesPhi(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return false;
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(argument => CarriesPhi(argument, seen));
        }

        if (type.IsArray)
        {
            return CarriesPhi(type.GetElementType()!, seen);
        }

        if (type.Namespace?.StartsWith("Prism", StringComparison.Ordinal) != true)
        {
            return false;
        }

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.GetCustomAttribute<PhiAttribute>() is not null || CarriesPhi(property.PropertyType, seen));
    }

    [Fact]
    public void Every_route_that_declares_a_phi_response_is_in_the_matrix_and_gated_by_its_policy()
    {
        using HttpClient warmup = factory.CreateClient();
        List<(string Route, string? Policy, List<Type> Produced)> routes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Select(endpoint => (
                endpoint.RoutePattern.RawText!,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).FirstOrDefault(policy => policy is not null),
                endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>().Select(m => m.Type).OfType<Type>().Where(t => t != typeof(void)).ToList()))
            .ToList();

        List<string> phiRoutes = routes.Where(r => r.Produced.Any(t => CarriesPhi(t, []))).Select(r => r.Route).ToList();

        // A route that declares a PHI response type must be in the matrix. The CSV export declares no type, so it is listed by hand.
        Assert.All(phiRoutes, route => Assert.Contains(route, PhiRoutes.Keys));
        foreach ((string route, string policy) in PhiRoutes)
        {
            (string Route, string? Policy, List<Type> Produced) match = routes.Single(r => r.Route == route);
            Assert.Equal(policy, match.Policy);
        }
    }

    [Fact]
    public void The_matrix_detector_does_flag_a_phi_type()
    {
        Assert.True(CarriesPhi(typeof(Prism.Api.Attention.AttentionClaimsResponse), []));
        Assert.False(CarriesPhi(typeof(Prism.Api.Attention.AttentionCountsResponse), []));
    }
}
