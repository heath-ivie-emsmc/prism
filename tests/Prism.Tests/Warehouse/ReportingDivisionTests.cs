using Prism.Application.Warehouse;

namespace Prism.Tests.Warehouse;

/// <summary>The stable key rule that turns a warehouse division name into the value the <c>division</c> filter accepts.</summary>
public sealed class ReportingDivisionTests
{
    [Theory]
    [InlineData("Mercy Flights Incorporated OR - Fixed", "mercy-flights-incorporated-or-fixed")]
    [InlineData("  Rotor  Base #2 ", "rotor-base-2")]
    [InlineData("A&B/Ground", "a-b-ground")]
    [InlineData("---", "division")]
    [InlineData("Ünïcode", "n-code")]
    public void The_key_is_lower_case_letters_and_digits_joined_by_single_hyphens(string name, string key) =>
        Assert.Equal(key, Assert.Single(ReportingDivision.FromNames([name])).Key);

    [Fact]
    public void A_long_name_gives_a_key_of_at_most_sixty_characters_matching_the_api_pattern()
    {
        ReportingDivision division = Assert.Single(ReportingDivision.FromNames([new string('a', 5000) + " b"]));

        Assert.Equal(ReportingDivision.MaxKeyLength, division.Key.Length);
        Assert.Matches("^[a-z0-9-]{1,60}$", division.Key);
    }

    [Fact]
    public void Names_that_give_the_same_key_get_numbered_in_name_order_so_keys_are_unique()
    {
        IReadOnlyList<ReportingDivision> divisions = ReportingDivision.FromNames(["Base 1", "Base-1", "Base_1", "Base 1"]);

        Assert.Equal(["base-1", "base-1-2", "base-1-3"], divisions.Select(d => d.Key));
        Assert.Equal(["Base 1", "Base-1", "Base_1"], divisions.Select(d => d.Name));
    }

    [Fact]
    public void A_collision_on_a_maximum_length_key_still_fits_in_sixty_characters()
    {
        string name = new('a', 80);
        IReadOnlyList<ReportingDivision> divisions = ReportingDivision.FromNames([name, name + "!"]);

        Assert.All(divisions, d => Assert.True(d.Key.Length <= ReportingDivision.MaxKeyLength));
        Assert.Equal(2, divisions.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void At_most_five_hundred_divisions_are_kept()
    {
        IReadOnlyList<ReportingDivision> divisions = ReportingDivision.FromNames(Enumerable.Range(0, 2000).Select(i => "Base " + i));

        Assert.Equal(ReportingDivision.MaxPerProvider, divisions.Count);
    }
}
