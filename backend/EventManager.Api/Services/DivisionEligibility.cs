using EventManager.Api.Persistence;

namespace EventManager.Api.Services;

/// <summary>A registrant profile projected to the fields division eligibility depends on.</summary>
public readonly record struct EligibilityProfile(double Weight, int Rank, int Age, string Gender);

/// <summary>
/// Pure, deterministic division-eligibility matching (Q3=A, BR-REG-3, BR-DIV-1). A division is
/// eligible iff the profile falls within its weight/rank/age bounds and gender matches. Determinism +
/// order-independence is the PBT-1 invariant. No I/O — testable in isolation.
/// </summary>
public static class DivisionEligibility
{
    public static bool IsEligible(EligibilityProfile p, DivisionRow d) =>
        (d.WeightLower is null || p.Weight >= d.WeightLower) &&
        p.Weight <= d.WeightUpper &&
        p.Rank >= d.MinRank && p.Rank <= d.MaxRank &&
        p.Age >= d.MinAge && p.Age <= d.MaxAge &&
        string.Equals(p.Gender, d.Gender, StringComparison.OrdinalIgnoreCase);

    /// <summary>Eligible division ids for a profile, in a stable order (ascending id) for determinism.</summary>
    public static IReadOnlyList<long> EligibleDivisionIds(EligibilityProfile p, IEnumerable<DivisionRow> divisions) =>
        divisions.Where(d => IsEligible(p, d)).Select(d => d.DivisionId).OrderBy(id => id).ToList();

    /// <summary>Age in whole years at a reference date (event date), from DOB.</summary>
    public static int AgeAt(DateOnly dob, DateOnly at)
    {
        var age = at.Year - dob.Year;
        if (dob > at.AddYears(-age)) age--;
        return age;
    }
}
