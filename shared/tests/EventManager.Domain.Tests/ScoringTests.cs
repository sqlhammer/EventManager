using EventManager.Domain;
using EventManager.Domain.Engines;
using FsCheck.Xunit;
using Xunit;

namespace EventManager.Domain.Tests;

public class ScoringTests
{
    private static PointSparringConfig NoEarlyFinish(PenaltyMode mode = PenaltyMode.AwardOpponent, int cap = 3)
        => new(new PenaltyPolicy(mode, cap));

    [Property] // BR-4.1 higher effective total wins (no penalties)
    public void PointSparring_HigherScoreWins(int pa, int pb)
    {
        int a = Math.Abs(pa % 20), b = Math.Abs(pb % 20);
        var o = new ScoringEngine()
            .ScorePointSparring(new PointSparringInput((Snowflake)1, a, 0, (Snowflake)2, b, 0), NoEarlyFinish())
            .Value;

        if (a > b) Assert.Equal((Snowflake)1, o.Winner);
        else if (b > a) Assert.Equal((Snowflake)2, o.Winner);
        else Assert.Null(o.Winner); // tie -> judge decision
    }

    [Fact] // BR-4.2 penalty cap => disqualification
    public void PointSparring_PenaltyCapDisqualifies()
    {
        var o = new ScoringEngine()
            .ScorePointSparring(new PointSparringInput((Snowflake)1, 10, 3, (Snowflake)2, 0, 0), NoEarlyFinish(cap: 3))
            .Value;
        Assert.Equal((Snowflake)2, o.Winner);
        Assert.Equal(MatchMethod.Disqualification, o.Method);
    }

    [Fact] // BR-4.3 DeductOffender never goes negative and awards correctly
    public void PointSparring_DeductOffenderFloorsAtZero()
    {
        var o = new ScoringEngine()
            .ScorePointSparring(new PointSparringInput((Snowflake)1, 1, 5, (Snowflake)2, 2, 0), NoEarlyFinish(PenaltyMode.DeductOffender, cap: 10))
            .Value;
        // A effective = max(0, 1-5)=0; B effective = 2 -> B wins
        Assert.Equal((Snowflake)2, o.Winner);
    }

    [Property] // BR-4.7 forms aggregate is invariant to score ordering
    public void Forms_AggregateOrderIndependent(int[]? raw)
    {
        var scores = (raw ?? Array.Empty<int>()).Select(x => (double)(Math.Abs(x % 100))).ToList();
        if (scores.Count == 0) return;
        var cfg = new FormsConfig();
        var a = ScoringEngine.Aggregate(scores, cfg);
        var b = ScoringEngine.Aggregate(Enumerable.Reverse(scores).ToList(), cfg);
        Assert.Equal(a, b, 10);
    }

    [Fact] // BR-4.6 drop high+low when judges >= 5
    public void Forms_DropsHighAndLowWithFiveJudges()
    {
        var scores = new List<double> { 1, 8, 8, 8, 20 }; // drop 1 and 20 -> avg of 8,8,8 = 8
        Assert.Equal(8.0, ScoringEngine.Aggregate(scores, new FormsConfig()), 10);
    }

    [Fact]
    public void Forms_NoDropWithFourJudges()
    {
        var scores = new List<double> { 2, 4, 6, 8 }; // avg all = 5
        Assert.Equal(5.0, ScoringEngine.Aggregate(scores, new FormsConfig()), 10);
    }
}
