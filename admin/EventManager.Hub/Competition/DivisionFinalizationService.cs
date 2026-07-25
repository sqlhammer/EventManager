using EventManager.Domain;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Competition;

/// <summary>
/// Division finalization (US-601). Computes placements from standings (wins desc, then losses asc),
/// stamps them, closes the division, and records the result as an event.
/// </summary>
public sealed class DivisionFinalizationService(HubDbContext db, HubEventWriter writer)
{
    public async Task<ErrorOr<IReadOnlyList<PlacementDto>>> FinalizeAsync(long eventId, long divisionId, CancellationToken ct = default)
    {
        var bracket = await db.Brackets.FindAsync([divisionId], ct);
        if (bracket is null) return Error.NotFound("Finalize.NoBracket", "No bracket for this division.");

        var standings = await db.Standings.Where(s => s.DivisionId == divisionId)
            .OrderByDescending(s => s.Wins).ThenBy(s => s.Losses).ToListAsync(ct);

        var placements = new List<PlacementDto>();
        var rank = 1;
        foreach (var standing in standings)
        {
            standing.Placement = rank;
            placements.Add(new PlacementDto(standing.RegistrationId, rank));
            rank++;
        }

        bracket.Status = nameof(DivisionStatus.Complete);
        await writer.AppendAsync(eventId, CompetitionEventTypes.DivisionFinalized, new DivisionFinalizedPayload(divisionId, placements), ct);
        await db.SaveChangesAsync(ct);
        return placements;
    }
}
