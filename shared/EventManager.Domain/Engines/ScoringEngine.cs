using ErrorOr;

namespace EventManager.Domain.Engines;

public interface IScoringEngine
{
    ErrorOr<MatchOutcome> ScorePointSparring(PointSparringInput input, PointSparringConfig config);
    ErrorOr<FormsResult> ScoreForms(FormsInput input, FormsConfig config);
}

/// <summary>
/// Point-sparring (Q1=A, Q2=D) and forms/kata (Q3=A) scoring. Pure; deterministic (BR-4.x).
/// </summary>
public sealed class ScoringEngine : IScoringEngine
{
    public ErrorOr<MatchOutcome> ScorePointSparring(PointSparringInput input, PointSparringConfig config)
    {
        if (config.PenaltyPolicy.Cap <= 0)
            return Error.Validation("Scoring.BadCap", "Penalty cap must be positive.");

        // Disqualification by penalty cap.
        bool aDq = input.PenaltiesA >= config.PenaltyPolicy.Cap;
        bool bDq = input.PenaltiesB >= config.PenaltyPolicy.Cap;
        if (aDq && bDq) return new MatchOutcome(null, MatchMethod.Disqualification, "Both disqualified");
        if (aDq) return new MatchOutcome(input.CompetitorB, MatchMethod.Disqualification, "A disqualified");
        if (bDq) return new MatchOutcome(input.CompetitorA, MatchMethod.Disqualification, "B disqualified");

        // Effective scores per configurable penalty policy.
        int a, b;
        if (config.PenaltyPolicy.Mode == PenaltyMode.AwardOpponent)
        {
            a = input.PointsA + input.PenaltiesB;
            b = input.PointsB + input.PenaltiesA;
        }
        else // DeductOffender, floored at 0 (BR-4.3)
        {
            a = Math.Max(0, input.PointsA - input.PenaltiesA);
            b = Math.Max(0, input.PointsB - input.PenaltiesB);
        }

        // Early finish (Q1): target score, then mercy gap.
        if (config.TargetScore is { } target)
        {
            if (a >= target && a > b) return new MatchOutcome(input.CompetitorA, MatchMethod.Points, "Target reached");
            if (b >= target && b > a) return new MatchOutcome(input.CompetitorB, MatchMethod.Points, "Target reached");
        }
        if (config.MercyGap is { } gap && Math.Abs(a - b) >= gap)
            return new MatchOutcome(a > b ? input.CompetitorA : input.CompetitorB, MatchMethod.Points, "Mercy gap");

        // Higher total wins; equal -> needs a judge decision.
        if (a > b) return new MatchOutcome(input.CompetitorA, MatchMethod.Points);
        if (b > a) return new MatchOutcome(input.CompetitorB, MatchMethod.Points);
        return new MatchOutcome(null, MatchMethod.Decision, "Tie — judge decision required");
    }

    public ErrorOr<FormsResult> ScoreForms(FormsInput input, FormsConfig config)
    {
        if (input.Competitors.Count == 0)
            return Error.Validation("Scoring.NoCompetitors", "No competitors to score.");

        var aggregates = input.Competitors
            .Select(c => (c.CompetitorId, Aggregate: Aggregate(c.JudgeScores, config)))
            .OrderByDescending(x => x.Aggregate)
            .ToList();

        var placements = new List<FormsPlacement>();
        for (int i = 0; i < aggregates.Count; i++)
            placements.Add(new FormsPlacement(aggregates[i].CompetitorId, aggregates[i].Aggregate, i + 1));

        return new FormsResult(placements);
    }

    /// <summary>Average of judge scores; drop one high + one low when judges &gt;= threshold (Q3).</summary>
    internal static double Aggregate(IReadOnlyList<double> scores, FormsConfig config)
    {
        if (scores.Count == 0) return 0d;
        IEnumerable<double> considered = scores;
        if (scores.Count >= config.DropHighLowWhenJudgesAtLeast)
        {
            var sorted = scores.OrderBy(s => s).ToList();
            considered = sorted.Skip(1).Take(sorted.Count - 2); // drop lowest + highest
        }
        var list = considered.ToList();
        return list.Count == 0 ? 0d : list.Average();
    }
}
