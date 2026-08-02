using EventManager.Contracts;
using EventManager.Hub.Persistence;
using EventManager.Sync;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Services;

/// <summary>
/// Spoke → hub sync intake (US-407 transport side). Rejects revoked-device writes (US-508), applies
/// events idempotently to the hub log (replays on reconnect never duplicate), and returns per-device
/// high-water marks. Mat-authority enforcement and competition projections belong to U4b.
/// </summary>
public sealed class SyncIntakeService(
    HubDbContext db, IEventStore store, DeviceRegistry devices,
    EventManager.Hub.Resilience.ReplicationSignal? replication = null)
{
    public async Task<ErrorOr<ReplicationAckDto>> IntakeAsync(long deviceId, ReplicationBatchDto batch, CancellationToken ct = default)
    {
        if (!await devices.IsActiveAsync(deviceId, ct))
            return Error.Forbidden("Sync.Revoked", "Device credential is revoked or unknown."); // US-508

        var ordered = batch.Events.Select(EventEnvelopeMapper.ToEvent).OrderBy(e => e.SequenceNumber).ToList();
        var accepted = 0;
        foreach (var evt in ordered)
            if (await store.AppendIfNotExistsAsync(evt, ct)) accepted++;
        await db.SaveChangesAsync(ct);

        // U10 (AD-Q5=C): spoke sync is where the bulk of an event log actually arrives, so this is
        // the signal that makes replication genuinely append-driven rather than timer-driven.
        if (accepted > 0) replication?.Signal();

        var hwm = new Dictionary<long, long>();
        foreach (var d in ordered.Select(e => e.DeviceId).Distinct())
            hwm[d] = await store.HighWaterMarkAsync(d, ct);
        return new ReplicationAckDto(accepted, hwm);
    }
}
