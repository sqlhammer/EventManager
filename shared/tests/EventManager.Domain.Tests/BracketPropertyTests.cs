using EventManager.Domain;
using EventManager.Domain.Engines;
using FsCheck.Xunit;
using Xunit;

namespace EventManager.Domain.Tests;

public class BracketPropertyTests
{
    private static int BoundedN(int raw) => 2 + (Math.Abs(raw % 31)); // 2..32

    [Property] // BR-3.1 participant preservation
    public void SingleElim_PreservesParticipants(int raw)
    {
        int n = BoundedN(raw);
        var b = new BracketEngine().GenerateSingleElimination((Snowflake)1, TestData.Seeds(n), TestData.Counter()).Value;

        var round0 = b.Matches.Where(m => m.RoundIndex == 0);
        var competitors = round0
            .SelectMany(m => new[] { m.CompetitorA, m.CompetitorB })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        Assert.Equal(n, competitors.Count);
        Assert.Equal(n, competitors.Distinct().Count());
    }

    [Property] // BR-3.2 byes == nextPow2(n) - n, to top seeds
    public void SingleElim_ByeCountMatchesFormula(int raw)
    {
        int n = BoundedN(raw);
        var b = new BracketEngine().GenerateSingleElimination((Snowflake)1, TestData.Seeds(n), TestData.Counter()).Value;

        int byes = b.Matches.Count(m => m.RoundIndex == 0 && ((m.CompetitorA is null) ^ (m.CompetitorB is null)));
        Assert.Equal(BracketEngine.NextPow2(n) - n, byes);
    }

    [Theory] // BR-3.3 exactly one champion
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(8)] [InlineData(11)] [InlineData(16)]
    public void SingleElim_ProducesExactlyOneChampion(int n)
    {
        var eng = new BracketEngine();
        var b = eng.GenerateSingleElimination((Snowflake)1, TestData.Seeds(n), TestData.Counter()).Value;

        while (true)
        {
            var ready = b.Matches.FirstOrDefault(m => m.Outcome is null && m.CompetitorA is not null && m.CompetitorB is not null);
            if (ready is null) break;
            b = eng.Advance(b, ready.MatchId, new MatchOutcome(ready.CompetitorA, MatchMethod.Points)).Value;
        }

        int lastRound = b.Matches.Max(m => m.RoundIndex);
        var final = b.Matches.Single(m => m.RoundIndex == lastRound);
        Assert.NotNull(final.Outcome);
        Assert.NotNull(final.Outcome!.Winner);
    }

    [Property] // BR-3.5 round-robin completeness
    public void RoundRobin_SchedulesEveryPairOnce(int raw)
    {
        int n = 2 + (Math.Abs(raw % 10)); // 2..11
        var b = new BracketEngine().GenerateRoundRobin((Snowflake)1, TestData.Seeds(n), TestData.Counter()).Value;
        Assert.Equal(n * (n - 1) / 2, b.Matches.Count);
    }
}
