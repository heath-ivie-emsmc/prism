using Prism.Application.Personas;

namespace Prism.Tests.Personas;

public sealed class PersonaScoringTests
{
    private static readonly PersonaCandidate[] Candidates = [new(1, 5), new(2, 1), new(3, 9)];

    [Fact]
    public void The_highest_total_wins()
    {
        Assert.Equal(3, PersonaScoring.Pick(Candidates, new Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 30 }));
    }

    [Fact]
    public void A_tie_goes_to_the_lower_sort_order()
    {
        Assert.Equal(2, PersonaScoring.Pick(Candidates, new Dictionary<int, int> { [1] = 20, [2] = 20, [3] = 5 }));
    }

    [Fact]
    public void A_tie_in_sort_order_goes_to_the_lower_id()
    {
        Assert.Equal(7, PersonaScoring.Pick([new PersonaCandidate(8, 1), new PersonaCandidate(7, 1)], new Dictionary<int, int>()));
    }

    [Fact]
    public void No_weights_picks_the_first_by_sort_order()
    {
        Assert.Equal(2, PersonaScoring.Pick(Candidates, new Dictionary<int, int>()));
    }

    [Fact]
    public void A_total_for_a_persona_that_is_not_a_candidate_is_ignored()
    {
        Assert.Equal(2, PersonaScoring.Pick(Candidates, new Dictionary<int, int> { [99] = 1000 }));
    }

    [Fact]
    public void No_candidate_picks_nothing()
    {
        Assert.Null(PersonaScoring.Pick([], new Dictionary<int, int> { [1] = 1 }));
    }
}
