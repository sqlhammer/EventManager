using System.Text.Json;
using EventManager.Contracts;
using EventManager.Domain;
using EventManager.Hub.Competition;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Tests;

/// <summary>U4b competition orchestration tests: bracket generation, advancement, mat authority,
/// weigh-in resolution, finalization, regeneration guard.</summary>
public sealed class CompetitionTests
{
    private static Registration Reg(long id, long divisionId, double weight, string academy)
    {
        var profile = new AthleteProfile((Snowflake)id, $"A{id}", new DateOnly(2000, 1, 1), 5, weight, academy, "M");
        return new Registration((Snowflake)id, (Snowflake)1, (Snowflake)id, [(Snowflake)divisionId], profile, PaymentStatus.Paid);
    }

    private static IReadOnlyList<Registration> Field(long divisionId, int n)
    {
        var list = new List<Registration>();
        for (int i = 1; i <= n; i++) list.Add(Reg(1000 + i, divisionId, 60 + i, $"Academy{i % 2}"));
        return list;
    }

    [Fact]
    public async Task Generate_creates_bracket_and_standings() // US-311/313
    {
        using var h = new HubTestHost();
        var result = await h.Brackets.GenerateAsync(eventId: 1, divisionId: 10, Field(10, 4), BracketFormat.SingleElimination);

        Assert.False(result.IsError);
        Assert.NotNull(await h.Db.Brackets.FindAsync(10L));
        Assert.Equal(4, await h.Db.Standings.CountAsync(s => s.DivisionId == 10));
    }

    [Fact]
    public async Task Cannot_regenerate_started_division() // US-314/408
    {
        using var h = new HubTestHost();
        await h.Brackets.GenerateAsync(1, 10, Field(10, 4), BracketFormat.SingleElimination);
        await h.Brackets.StartAsync(1, 10);

        var regen = await h.Brackets.GenerateAsync(1, 10, Field(10, 4), BracketFormat.SingleElimination);
        Assert.True(regen.IsError);
    }

    [Fact]
    public async Task Advance_records_a_win() // US-404
    {
        using var h = new HubTestHost();
        await h.Brackets.GenerateAsync(1, 10, Field(10, 2), BracketFormat.SingleElimination);
        var match = FirstPlayableMatch(h, 10);

        var advance = await h.Brackets.AdvanceAsync(1, 10, match.MatchId, match.CompetitorA!.Value, MatchMethod.Points);
        Assert.False(advance.IsError);

        var winnerStanding = await h.Db.Standings.FirstAsync(s => s.DivisionId == 10 && s.RegistrationId == match.CompetitorA!.Value);
        Assert.Equal(1, winnerStanding.Wins);
    }

    [Fact]
    public async Task Foreign_mat_score_is_rejected() // US-406
    {
        using var h = new HubTestHost();
        await h.Brackets.GenerateAsync(1, 10, Field(10, 2), BracketFormat.SingleElimination);
        var match = FirstPlayableMatch(h, 10);

        // A paired device NOT assigned to division 10.
        var qr = await h.Pairing.IssueTokenAsync(1, "Judge — Mat 9");
        var paired = await h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "s"));
        var deviceId = paired.Value.DeviceId;

        var input = new PointSparringInput((Snowflake)match.CompetitorA!.Value, 5, 0, (Snowflake)match.CompetitorB!.Value, 3, 0);
        var config = new PointSparringConfig(new PenaltyPolicy(PenaltyMode.AwardOpponent, 3));

        var result = await h.Scoring.SubmitPointSparringAsync(deviceId, 1, 10, match.MatchId, input, config);
        Assert.True(result.IsError);   // mat authority denies foreign-mat write
    }

    [Fact]
    public async Task Assigned_mat_score_advances_bracket() // US-404/406 happy path
    {
        using var h = new HubTestHost();
        await h.Brackets.GenerateAsync(1, 10, Field(10, 2), BracketFormat.SingleElimination);
        var match = FirstPlayableMatch(h, 10);

        var qr = await h.Pairing.IssueTokenAsync(1, "Judge — Mat 1");
        var paired = await h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "s"));
        await h.Devices.AssignMatAsync(paired.Value.DeviceId, 10);

        var input = new PointSparringInput((Snowflake)match.CompetitorA!.Value, 7, 0, (Snowflake)match.CompetitorB!.Value, 2, 0);
        var config = new PointSparringConfig(new PenaltyPolicy(PenaltyMode.AwardOpponent, 3));

        var result = await h.Scoring.SubmitPointSparringAsync(paired.Value.DeviceId, 1, 10, match.MatchId, input, config);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Strict_weigh_in_over_limit_disqualifies() // US-308
    {
        using var h = new HubTestHost();
        var division = new Division((Snowflake)10, (Snowflake)1,
            new DivisionCriteria(new WeightClass(null, 70), new RankRange(0, 100), new AgeRange(0, 120), "M"),
            BracketFormat.SingleElimination, DivisionStatus.NotStarted);
        var policy = new WeighInPolicy(WeighInPolicyMode.Strict);

        var result = await h.WeighIn.ResolveAsync(1, athleteId: 500, weight: 75, division, policy, []);
        Assert.False(result.IsError);
        Assert.Equal(WeighInResult.Disqualified, result.Value.Result);
    }

    [Fact]
    public async Task Finalize_assigns_placements_by_wins() // US-601
    {
        using var h = new HubTestHost();
        await h.Brackets.GenerateAsync(1, 10, Field(10, 2), BracketFormat.SingleElimination);
        var match = FirstPlayableMatch(h, 10);
        await h.Brackets.AdvanceAsync(1, 10, match.MatchId, match.CompetitorA!.Value, MatchMethod.Points);

        var placements = await h.Finalization.FinalizeAsync(1, 10);
        Assert.False(placements.IsError);
        Assert.Equal(match.CompetitorA!.Value, placements.Value[0].RegistrationId);  // winner placed first
    }

    private static MatchDto FirstPlayableMatch(HubTestHost h, long divisionId)
    {
        var row = h.Db.Brackets.AsNoTracking().First(b => b.DivisionId == divisionId);
        var matches = JsonSerializer.Deserialize<List<MatchDto>>(row.MatchesJson)!;
        return matches.First(m => m.CompetitorA is not null && m.CompetitorB is not null);
    }
}
