using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Sync;
using ErrorOr;

namespace EventManager.Hub.Competition;

/// <summary>Dispute flag + resolution (US-405). A judge flags a match; the organizer resolves it,
/// each step recorded as an event.</summary>
public sealed class DisputeService(HubDbContext db, HubEventWriter writer, IIdGenerator ids)
{
    public async Task<ErrorOr<long>> FlagAsync(long eventId, long divisionId, long matchId, string reason, CancellationToken ct = default)
    {
        var disputeId = ids.NextId();
        db.Disputes.Add(new DisputeRow { DisputeId = disputeId, DivisionId = divisionId, MatchId = matchId, Reason = reason, Status = "Open" });
        await writer.AppendAsync(eventId, CompetitionEventTypes.DisputeFlagged,
            new DisputeFlaggedPayload(disputeId, divisionId, matchId, reason), ct);
        await db.SaveChangesAsync(ct);
        return disputeId;
    }

    public async Task<ErrorOr<Success>> ResolveAsync(long eventId, long disputeId, string resolution, CancellationToken ct = default)
    {
        var dispute = await db.Disputes.FindAsync([disputeId], ct);
        if (dispute is null) return Error.NotFound("Dispute.NotFound", "Dispute not found.");
        dispute.Status = "Resolved";
        dispute.Resolution = resolution;
        await writer.AppendAsync(eventId, CompetitionEventTypes.DisputeResolved, new DisputeResolvedPayload(disputeId, resolution), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }
}
