using EventManager.Api.Auth;
using EventManager.Api.Persistence;
using EventManager.Api.Projections;
using EventManager.Contracts;
using EventManager.Domain;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// S-7 replication ingest (US-504). Sequence-ordered idempotent append (RP-1, BR-ING-2), synchronous
/// projection fold (incl. ResultsProjection). The cloud is a mirror — it never conflicts with the hub.
/// Returns an ack with per-device high-water marks so the hub resumes gap-free.
///
/// U10 (AD-Q3=A): the caller is now an <see cref="IngestCaller"/> rather than a bare account id.
/// An account caller authorizes exactly as before, via <see cref="OrganizerAction.ManageRoster"/> on
/// every scope in the batch. A hub-credential caller authorizes against its own bound scope and
/// nothing else, and stamps ingest provenance on each newly appended event (BR-REPL-19..21).
/// </summary>
public sealed class IngestService(AppDbContext db, CloudProjectionHost projections, EventAuthorizer authz)
{
    public async Task<ErrorOr<ReplicationAckDto>> IngestAsync(
        IngestCaller caller, ReplicationBatchDto batch, CancellationToken ct = default)
    {
        var scopes = batch.Events.Select(e => e.EventScopeId).Distinct().ToList();

        var authorized = await AuthorizeScopesAsync(caller, scopes, ct);
        if (authorized.IsError) return authorized.Errors;

        var ordered = batch.Events
            .Select(EventEnvelopeMapper.ToEvent)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        var provenance = ProvenanceOf(caller);

        var accepted = 0;
        foreach (var evt in ordered)
        {
            var exists = await db.Events.AnyAsync(e => e.DeviceId == evt.DeviceId && e.SequenceNumber == evt.SequenceNumber, ct);
            if (exists) continue;  // idempotent — replay never duplicates, and never rewrites provenance (BR-REPL-20)

            var record = new EventRecord
            {
                EventId = evt.EventId, DeviceId = evt.DeviceId, SequenceNumber = evt.SequenceNumber,
                EventType = evt.EventType, SchemaVersion = evt.SchemaVersion, Payload = evt.Payload.ToArray(),
                OccurredAt = evt.OccurredAt, EventScopeId = evt.EventScopeId,
                IngestedByCredentialId = provenance,
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

    /// <summary>
    /// Per-device cloud high-water marks for one event scope (U10-FR-12, BR-REPL-11). The hub seeds
    /// its cursors from this at startup so a restart does not re-send the whole event (US-805).
    /// </summary>
    public async Task<ErrorOr<IReadOnlyDictionary<long, long>>> HighWaterMarksAsync(
        IngestCaller caller, long eventScopeId, CancellationToken ct = default)
    {
        var authorized = await AuthorizeScopesAsync(caller, [eventScopeId], ct);
        if (authorized.IsError) return authorized.Errors;

        var deviceIds = await db.Events.AsNoTracking()
            .Where(e => e.EventScopeId == eventScopeId)
            .Select(e => e.DeviceId).Distinct().ToListAsync(ct);

        var hwm = new Dictionary<long, long>();
        foreach (var deviceId in deviceIds) hwm[deviceId] = await HighWaterMarkAsync(deviceId, ct);
        return hwm;
    }

    /// <summary>
    /// BR-REPL-10/13. A batch spanning any scope the caller may not write is refused ENTIRELY — a
    /// batch is the unit of atomic intent, and accepting the in-scope half would leave the hub's
    /// cursor arithmetic describing something that did not happen.
    /// </summary>
    private async Task<ErrorOr<Success>> AuthorizeScopesAsync(
        IngestCaller caller, IReadOnlyList<long> scopes, CancellationToken ct)
    {
        if (caller is IngestCaller.Hub hub)
        {
            foreach (var scope in scopes)
            {
                if (scope != hub.EventScopeId)
                    return Error.Forbidden("Ingest.Scope", $"Not authorized to ingest for event {scope}.");
            }
            return Result.Success;
        }

        if (caller is IngestCaller.Account account)
        {
            foreach (var scope in scopes)
            {
                if (!await authz.IsPermittedAsync(account.AccountId, scope, OrganizerAction.ManageRoster, ct))
                    return Error.Forbidden("Ingest.Scope", $"Not authorized to ingest for event {scope}.");
            }
            return Result.Success;
        }

        return Error.Forbidden("Ingest.Caller", "Unrecognized caller.");
    }

    /// <summary>Cloud-authored events have no delivering hub, so provenance stays null (BR-REPL-21).</summary>
    private static long? ProvenanceOf(IngestCaller caller)
    {
        if (caller is IngestCaller.Hub hub) return hub.CredentialId;
        return null;
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
