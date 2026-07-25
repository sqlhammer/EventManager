using ErrorOr;

namespace EventManager.Domain.Engines;

public interface IWeighInPolicyEvaluator
{
    ErrorOr<WeighInOutcome> Evaluate(double weight, Division division, WeighInPolicy policy, IReadOnlyList<Division> autoMoveCandidates);
}

/// <summary>
/// Weigh-in policy evaluation (FR-5.3, Q6=A, BR-5.x). Under lower bound always passes;
/// tolerance is a percent of the upper bound, over-limit only. Pure function.
/// </summary>
public sealed class WeighInPolicyEvaluator : IWeighInPolicyEvaluator
{
    public ErrorOr<WeighInOutcome> Evaluate(double weight, Division division, WeighInPolicy policy, IReadOnlyList<Division> autoMoveCandidates)
    {
        var wc = division.Criteria.WeightClass;

        // Within class (including under lower bound) always passes.
        if (weight <= wc.UpperBound)
            return new WeighInOutcome(WeighInResult.Pass);

        switch (policy.Mode)
        {
            case WeighInPolicyMode.Strict:
                return new WeighInOutcome(WeighInResult.Disqualified);

            case WeighInPolicyMode.Tolerance:
                if (policy.TolerancePercent is not { } pct)
                    return Error.Validation("WeighIn.NoTolerance", "Tolerance policy requires a percent.");
                double cap = wc.UpperBound * (1 + pct / 100.0);
                if (weight <= cap) return new WeighInOutcome(WeighInResult.TolerancePass);
                return new WeighInOutcome(WeighInResult.Disqualified);

            case WeighInPolicyMode.AutoMove:
                var target = autoMoveCandidates.FirstOrDefault(d =>
                    d.DivisionId != division.DivisionId &&
                    d.Status == DivisionStatus.NotStarted &&
                    weight <= d.Criteria.WeightClass.UpperBound &&
                    weight >= (d.Criteria.WeightClass.LowerBound ?? double.MinValue));
                if (target is null) return new WeighInOutcome(WeighInResult.Disqualified);
                return new WeighInOutcome(WeighInResult.Moved, target.DivisionId);

            default:
                return Error.Unexpected("WeighIn.UnknownMode", "Unknown weigh-in policy mode.");
        }
    }
}
