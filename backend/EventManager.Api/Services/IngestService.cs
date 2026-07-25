using EventManager.Api.Auth;
using EventManager.Api.Persistence;
using EventManager.Api.Projections;
using EventManager.Contracts;
using EventManager.Domain;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// S-7 replication ingest (US-504). Event-scoped authz (Q7=A, BR-ING-1), sequence-ordered idempotent
/// append (RP-1, BR-ING-2), synchronous projection fold (incl. ResultsProjection). The cloud is a
/// mirror — it never conflicts with the hub. Returns an ack with per-device high-water marks so the
/// hub resumes gap-free.
/// </summary>
public sealed class IngestService(AppDbContext db, CloudProjectionHost projections, EventAuthorizer authz)
{
    public async Task<ErrorOr<ReplicationAckDto>> IngestAsync(long callerAccountId, ReplicationBatchDto batch, CancellationToken ct = default)
    {
        // Event-scoped authorization: the caller must be an organizer on every scope in the batch.
        var scopes = batch.Events.Select(e => e.EventScopeId).Distinct().ToList();
        foreach (var scope in scopes)
            if (!await authz.IsPermittedAsync(callerAccountId, scope, OrganizerAction.ManageRoster, ct))
                return Error.Forbidden("Ingest.Scope", $"Not authorized to ingest for event {scope}.");

        var ordered = batch.Events
            .Select(EventEnvelopeMapper.ToEvent)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var accepted = 0;
        foreach (var evt in ordered)
        {
            var exists = await db.Events.AnyAsync(e => e.DeviceId == evt.DeviceId && e.SequenceNumber == evt.SequenceNumber, ct);
            if (exists) continue;  // idempotent — replay never duplicates

            var record = new EventRecord
            {
                EventId = evt.EventId, DeviceId = evt.DeviceId, SequenceNumber = evt.SequenceNumber,
                EventType = evt.EventType, SchemaVersion = evt.SchemaVersion, Payload = evt.Payload.ToArray(),
                OccurredAt = evt.OccurredAt, EventScopeId = evt.EventScopeId,
            };
            db.Events.Add(record);
            projections.Dispatch(record);   // folds ResultsProjection for known result types; ignores others (BR-ING-3)
            accepted++;
        }
        await db.SaveChangesAsync(ct);

        // Per-device gap-free high-water marks
        var hwm = new Dictionary<long, long>();
        foreach (var deviceId in ordered.Select(e => e.DeviceId).Distinct())
            hwm[deviceId] = await HighWaterMarkAsync(deviceId, ct);

        return new ReplicationAckDto(accepted, hwm);
    }

    private async Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct)
    {
        var seqs = await db.Events.AsNoTracking().Where(e => e.DeviceId == deviceId)
            .OrderBy(e => e.SequenceNumber).Select(e => e.SequenceNumber).ToListAsync(ct);
        long hwm = 0;
        foreach (var s in seqs) { if (s == hwm + 1) hwm = s; else if (s > hwm + 1) break; }
        return hwm;
    }
}
