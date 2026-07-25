using EventManager.Api.Persistence;
using EventManager.Api.Services;
using FsCheck.Xunit;

namespace EventManager.Api.Tests;

/// <summary>PBT-1 (U3-NFR-T2): division-assignment determinism and order-independence (BR-REG-3).</summary>
public sealed class DivisionEligibilityTests
{
    private static DivisionRow Div(long id, double upper, string gender = "M") => new()
    {
        DivisionId = id, EventId = 1, WeightLower = null, WeightUpper = upper,
        MinRank = 0, MaxRank = 100, MinAge = 0, MaxAge = 120, Gender = gender,
    };

    [Property(MaxTest = 200)] // PBT-1 order-independence
    public void Eligibility_is_order_independent(int weightSeed, int count)
    {
        var weight = 40 + Math.Abs(weightSeed % 120);
        var n = 1 + Math.Abs(count % 8);
        var divisions = Enumerable.Range(1, n).Select(i => Div(i, 50 + i * 10)).ToList();
        var profile = new EligibilityProfile(weight, 5, 25, "M");

        var forward = DivisionEligibility.EligibleDivisionIds(profile, divisions);
        var reversed = DivisionEligibility.EligibleDivisionIds(profile, Enumerable.Reverse(divisions));

        Assert.True(forward.SequenceEqual(reversed));
    }

    [Property(MaxTest = 200)] // PBT-1 determinism
    public void Eligibility_is_deterministic(int weightSeed)
    {
        var weight = 40 + Math.Abs(weightSeed % 120);
        var divisions = new[] { Div(1, 60), Div(2, 80), Div(3, 100) };
        var profile = new EligibilityProfile(weight, 5, 25, "M");
        var a = DivisionEligibility.EligibleDivisionIds(profile, divisions);
        var b = DivisionEligibility.EligibleDivisionIds(profile, divisions);
        Assert.True(a.SequenceEqual(b));
    }

    [Fact]
    public void Gender_mismatch_is_never_eligible()
    {
        var profile = new EligibilityProfile(70, 5, 25, "F");
        Assert.False(DivisionEligibility.IsEligible(profile, Div(1, 100, "M")));
    }

    [Fact]
    public void Overweight_is_never_eligible()
    {
        var profile = new EligibilityProfile(120, 5, 25, "M");
        Assert.False(DivisionEligibility.IsEligible(profile, Div(1, 100)));
    }
}
