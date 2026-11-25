namespace Prism.Application.Personas;

/// <summary>A Persona that can be chosen: its id and the order that breaks ties.</summary>
public readonly record struct PersonaCandidate(int Id, int SortOrder);

/// <summary>Picks one Persona from the weights of the options a user chose. Pure and deterministic, so it needs no stored answers.</summary>
public static class PersonaScoring
{
    /// <summary>The highest total wins and a tie goes to the lower sort order, then the lower id. Null when there is no candidate.</summary>
    public static int? Pick(IReadOnlyList<PersonaCandidate> candidates, IReadOnlyDictionary<int, int> totals)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(totals);

        return candidates.Count == 0
            ? null
            : candidates
                .OrderByDescending(c => totals.GetValueOrDefault(c.Id))
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .First().Id;
    }
}
