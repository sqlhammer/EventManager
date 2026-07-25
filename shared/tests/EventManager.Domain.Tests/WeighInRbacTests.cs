using EventManager.Domain;
using EventManager.Domain.Engines;
using FsCheck.Xunit;
using Xunit;

namespace EventManager.Domain.Tests;

public class WeighInRbacTests
{
    [Property] // BR-5.2/5.4 tolerance boundary (% of upper, over-only), inclusive at cap
    public void WeighIn_ToleranceBoundary(int rawW)
    {
        const double upper = 70.0, pct = 2.0;
        double cap = upper * (1 + pct / 100.0);
        double w = 60.0 + (Math.Abs(rawW % 2000)) / 100.0; // 60.00 .. 79.99

        var div = TestData.DivisionWithUpper(upper);
        var policy = new WeighInPolicy(WeighInPolicyMode.Tolerance, pct);
        var o = new WeighInPolicyEvaluator().Evaluate(w, div, policy, Array.Empty<Division>()).Value;

        if (w <= upper) Assert.Equal(WeighInResult.Pass, o.Result);
        else if (w <= cap) Assert.Equal(WeighInResult.TolerancePass, o.Result);
        else Assert.Equal(WeighInResult.Disqualified, o.Result);
    }

    [Fact] // BR-5.3 strict => DQ over limit
    public void WeighIn_StrictDisqualifiesOverLimit()
    {
        var div = TestData.DivisionWithUpper(70.0);
        var o = new WeighInPolicyEvaluator()
            .Evaluate(70.5, div, new WeighInPolicy(WeighInPolicyMode.Strict), Array.Empty<Division>()).Value;
        Assert.Equal(WeighInResult.Disqualified, o.Result);
    }

    [Fact] // BR-5.5 auto-move to a fitting, not-started division
    public void WeighIn_AutoMoveFindsTarget()
    {
        var source = TestData.DivisionWithUpper(70.0, id: 1);
        var target = TestData.DivisionWithUpper(80.0, id: 2);
        var o = new WeighInPolicyEvaluator()
            .Evaluate(75.0, source, new WeighInPolicy(WeighInPolicyMode.AutoMove), new[] { target }).Value;
        Assert.Equal(WeighInResult.Moved, o.Result);
        Assert.Equal((Snowflake)2, o.TargetDivisionId);
    }

    [Fact] // BR-6.x RBAC deny-by-default + Full-Admin-only set
    public void Rbac_EnforcesFullAdminOnlyActions()
    {
        var pol = new RoleAuthorizationPolicy();
        var co = new OrganizerRoleAssignment((Snowflake)1, (Snowflake)2, (Snowflake)3, OrganizerRole.CoOrganizer);
        var fa = co with { Role = OrganizerRole.FullAdmin };

        Assert.False(pol.IsPermitted(co, OrganizerAction.DeleteEvent));
        Assert.True(pol.IsPermitted(fa, OrganizerAction.DeleteEvent));
        Assert.True(pol.IsPermitted(co, OrganizerAction.GenerateBracket));
        Assert.True(pol.IsPermitted(fa, OrganizerAction.GenerateBracket));
        Assert.False(pol.IsPermitted(null, OrganizerAction.GenerateBracket)); // deny by default
    }
}
