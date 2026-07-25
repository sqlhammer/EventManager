using System.Text.Json;
using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Sync;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Competition;

/// <summary>
/// Bracket lifecycle on the hub (US-311/312/313/314/404/408). Orchestrates the U1 seeding + bracket
/// engines; generation/advancement/finalization are auditable events. Regeneration is allowed only
/// before the division starts (US-314); after start, structural change is an explicit organizer action.
/// </summary>
public sealed class BracketService(HubDbContext db, HubEventWriter writer, IIdGenerator ids,
    ISeedingEngine seeding, IBracketEngine brackets)
{
    public async Task<ErrorOr<Success>> GenerateAsync(long eventId, long divisionId,
        IReadOnlyList<Registration> registrations, BracketFormat format, CancellationToken ct = default)
    {
        var existing = await db.Brackets.FindAsync([divisionId], ct);
        if (existing is not null && existing.Status != nameof(DivisionStatus.NotStarted))
            return Error.Conflict("Bracket.Started", "Cannot regenerate a started division.");   // US-314/408

        var seededResult = seeding.Seed(registrations, new SeedingOptions());
        if (seededResult.IsError) return seededResult.Errors;

        ErrorOr<Bracket> genResult;
        if (format == BracketFormat.RoundRobin)
            genResult = brackets.GenerateRoundRobin((Snowflake)divisionId, seededResult.Value, () => (Snowflake)ids.NextId());
        else
            genResult = brackets.GenerateSingleElimination((Snowflake)divisionId, seededResult.Value, () => (Snowflake)ids.NextId());
        if (genResult.IsError) return genResult.Errors;

        var matches = BracketMapper.ToDtos(genResult.Value.Matches);
        await UpsertBracketAsync(divisionId, eventId, format, nameof(DivisionStatus.NotStarted), matches, ct);

        // (Re)initialise standings for the seeded field.
        var old = db.Standings.Where(s => s.DivisionId == divisionId);
        db.Standings.RemoveRange(old);
        foreach (var seed in seededResult.Value)
            db.Standings.Add(new StandingRow { Id = ids.NextId(), DivisionId = divisionId, RegistrationId = seed.RegistrationId.Value });

        await writer.AppendAsync(eventId, CompetitionEventTypes.BracketGenerated,
            new BracketGeneratedPayload(divisionId, eventId, format.ToString(), matches), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> StartAsync(long eventId, long divisionId, CancellationToken ct = default)
    {
        var bracket = await db.Brackets.FindAsync([divisionId], ct);
        if (bracket is null) return Error.NotFound("Bracket.NotFound", "No bracket for this division.");
        bracket.Status = nameof(DivisionStatus.Started);
        await writer.AppendAsync(eventId, CompetitionEventTypes.DivisionStarted, new DivisionStartedPayload(divisionId), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> AdvanceAsync(long eventId, long divisionId, long matchId, long winnerId,
        MatchMethod method, CancellationToken ct = default)
    {
        var row = await db.Brackets.FindAsync([divisionId], ct);
        if (row is null) return Error.NotFound("Bracket.NotFound", "No bracket for this division.");

        var matches = BracketMapper.ToMatches(Deserialize(row.MatchesJson));
        var bracket = new Bracket((Snowflake)ids.NextId(), (Snowflake)divisionId,
            Enum.Parse<BracketFormat>(row.Format), [], matches, Enum.Parse<DivisionStatus>(row.Status));

        var advanced = brackets.Advance(bracket, (Snowflake)matchId, new MatchOutcome((Snowflake)winnerId, method));
        if (advanced.IsError) return advanced.Errors;

        row.MatchesJson = Serialize(BracketMapper.ToDtos(advanced.Value.Matches));
        if (row.Status == nameof(DivisionStatus.NotStarted)) row.Status = nameof(DivisionStatus.Started);

        // Standings: winner +1 win; the beaten competitor +1 loss.
        var match = matches.First(m => m.MatchId.Value == matchId);
        long? loserId = match.CompetitorA?.Value;
        if (match.CompetitorA?.Value == winnerId) loserId = match.CompetitorB?.Value;
        Bump(divisionId, winnerId, win: true);
        if (loserId is { } l) Bump(divisionId, l, win: false);

        await writer.AppendAsync(eventId, CompetitionEventTypes.BracketAdvanced,
            new BracketAdvancedPayload(divisionId, matchId, winnerId, method.ToString()), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    private void Bump(long divisionId, long registrationId, bool win)
    {
        var s = db.Standings.Local.FirstOrDefault(x => x.DivisionId == divisionId && x.RegistrationId == registrationId)
            ?? db.Standings.FirstOrDefault(x => x.DivisionId == divisionId && x.RegistrationId == registrationId);
        if (s is null) { s = new StandingRow { Id = ids.NextId(), DivisionId = divisionId, RegistrationId = registrationId }; db.Standings.Add(s); }
        if (win) s.Wins++; else s.Losses++;
    }

    private async Task UpsertBracketAsync(long divisionId, long eventId, BracketFormat format, string status, IReadOnlyList<MatchDto> matches, CancellationToken ct)
    {
        var row = await db.Brackets.FindAsync([divisionId], ct);
        if (row is null)
            db.Brackets.Add(new BracketRow { DivisionId = divisionId, EventId = eventId, Format = format.ToString(), Status = status, MatchesJson = Serialize(matches) });
        else { row.Format = format.ToString(); row.Status = status; row.MatchesJson = Serialize(matches); }
    }

    private static string Serialize(IReadOnlyList<MatchDto> m) => JsonSerializer.Serialize(m);
    private static IReadOnlyList<MatchDto> Deserialize(string json) => JsonSerializer.Deserialize<List<MatchDto>>(json) ?? [];
}
