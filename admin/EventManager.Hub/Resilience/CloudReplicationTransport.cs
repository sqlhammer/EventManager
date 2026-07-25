using EventManager.Contracts;
using EventManager.Sync;
using Microsoft.EntityFrameworkCore;
using EventManager.Hub.Persistence;

namespace EventManager.Hub.Resilience;

/// <summary>
/// Transport the hub uses to replicate its log to the cloud (US-504). The real adapter POSTs
/// <see cref="ReplicationBatchDto"/> to the cloud <c>EventIngestController</c>; it is a deferred seam.
/// <see cref="IsOnline"/> gates replication so an outage is a no-op that resumes on reconnect.
/// </summary>
public interface ICloudReplicationTransport
{
    bool IsOnline { get; }
    Task<ReplicationAckDto> SendAsync(ReplicationBatchDto batch, CancellationToken ct = default);
}

/// <summary>
/// In-process / loopback transport backed by an <see cref="IEventStore"/> that stands in for the cloud
/// mirror. Applies events idempotently and returns per-device high-water marks — the same contract the
/// real cloud ingest honors. Used as the local loopback and by the resilience integration tests.
/// </summary>
public sealed class StoreBackedReplicationTransport(HubDbContext cloudDb, IEventStore cloudStore) : ICloudReplicationTransport
{
    public bool IsOnline { get; set; } = true;

    public async Task<ReplicationAckDto> SendAsync(ReplicationBatchDto batch, CancellationToken ct = default)
    {
        if (!IsOnline) throw new InvalidOperationException("Cloud transport is offline.");

        var ordered = batch.Events.Select(EventEnvelopeMapper.ToEvent).OrderBy(e => e.SequenceNumber).ToList();
        var accepted = 0;
        foreach (var evt in ordered)
        {
            if (await cloudStore.AppendIfNotExistsAsync(evt, ct)) accepted++;
        }
        await cloudDb.SaveChangesAsync(ct);

        var hwm = new Dictionary<long, long>();
        foreach (var deviceId in ordered.Select(e => e.DeviceId).Distinct())
            hwm[deviceId] = await cloudStore.HighWaterMarkAsync(deviceId, ct);
        return new ReplicationAckDto(accepted, hwm);
    }
}
