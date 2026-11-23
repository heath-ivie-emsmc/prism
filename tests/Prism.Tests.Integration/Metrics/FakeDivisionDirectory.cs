using Prism.Application.Warehouse;

namespace Prism.Tests.Integration.Metrics;

/// <summary>Stands in for the Provider division directory: Providers 200 and 300 have Facilities 9 and 10, everyone else has none.</summary>
public sealed class FakeDivisionDirectory : IProviderDivisionDirectory
{
    public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(portalCompanyId is 200 or 300 ? new HashSet<string> { "9", "10" } : []);

        public static IReadOnlyList<ReportingDivision> Divisions { get; } = [new("fixed-base", "Mercy Flights - Fixed"), new("rotor-base", "Mercy Flights - Rotor")];

        /// <summary>The Providers whose divisions were looked up, in order.</summary>
        public List<int> Lookups { get; } = [];

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken)
        {
            Lookups.Add(portalCompanyId);
            return Task.FromResult<IReadOnlyList<ReportingDivision>>(portalCompanyId is 100 or 101 ? [.. Divisions] : []);
        }
}
