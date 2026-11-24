using System.Text.RegularExpressions;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Tests.Metrics;

/// <summary>The origin modifier mapping is written twice, once in SQL and once as the table the API and the docs read. These tests keep the two in step.</summary>
public sealed partial class PickupSourceMappingTests
{
    [GeneratedRegex(@"WHEN '([A-Z])' THEN '([a-z-]+)'", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Mapping();

    [Fact]
    public void The_sql_maps_exactly_the_letters_of_the_source_table_to_the_same_keys()
    {
        (string Letter, string Key)[] sql = [.. Mapping().Matches(PickupSql.SourceCase).Select(m => (m.Groups[1].Value, m.Groups[2].Value))];
        (string Letter, string Key)[] table = [.. ITransportsByPickupLocationMetric.Sources.SelectMany(s => s.Mods.Select(c => (c.ToString(), s.Key)))];

        Assert.Equal([.. table.OrderBy(t => t.Letter, StringComparer.Ordinal)], [.. sql.OrderBy(t => t.Letter, StringComparer.Ordinal)]);
        Assert.Equal(["D", "E", "G", "H", "I", "J", "N", "P", "R", "S"], sql.Select(t => t.Letter).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Scene_residence_and_custodial_facility_never_list_facilities_and_the_facility_sql_list_matches_the_table()
    {
        string[] facilityBased = [.. ITransportsByPickupLocationMetric.Sources.Where(s => s.FacilityBased).Select(s => s.Key)];

        Assert.DoesNotContain(facilityBased, k => k is "scene" or "residence" or "custodial-facility" or "other");
        Assert.All(facilityBased, k => Assert.True(PickupSql.FacilityBasedKeys.Contains($"'{k}'", StringComparison.Ordinal), k));
        Assert.Equal(facilityBased.Length, Regex.Count(PickupSql.FacilityBasedKeys, "'", RegexOptions.None, TimeSpan.FromSeconds(1)) / 2);
        Assert.Equal("other", ITransportsByPickupLocationMetric.Sources[^1].Key);
        Assert.Equal(ITransportsByPickupLocationMetric.Sources.Count, ITransportsByPickupLocationMetric.Sources.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_pickup_sql_never_names_an_address_city_state_zip_or_patient_column()
    {
        string[] forbidden = ["origin_address", "origin_city", "origin_state", "origin_zip", "patient_", "first_name", "last_name", "birth", "ssn", "narrative", "dob"];
        string[] sql = [TransportsByPickupLocationMetric.SourcesSql, TransportsByPickupLocationMetric.FacilitiesSql, TransportsByPickupLocationMetric.CashSql, PickupSql.SourceCase, PickupSql.FacilityBasedKeys, TripSql.CleanFacility];

        Assert.All(sql, text => Assert.All(forbidden, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase)));
    }
}
