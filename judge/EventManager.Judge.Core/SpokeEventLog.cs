using EventManager.Sync;

namespace EventManager.Judge.Core;

/// <summary>
/// The spoke write path: durable-before-ack (NFR-1.1, BR-CS-1). Mints a Snowflake id (U1
/// <see cref="IIdGenerator"/> in the judge worker range), assigns a contiguous per-device sequence,
/// and persists to the local store BEFORE returning — the caller acks the UI only after this. Queued
/// events sync to the hub idempotently on connectivity (U2 ClientSync).
/// </summary>
public sealed class SpokeEventLog(IEventStore store, IIdGenerator ids, IEventSerializer serializer, long deviceId)
{
    private long _next = -1;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TournamentEvent> AppendDurableAsync<T>(long scopeId, string eventType, T payload, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_next < 0) _next = await store.HighWaterMarkAsync(deviceId, ct) + 1;
            var evt = new TournamentEvent(ids.NextId(), deviceId, _next, eventType, 1,
                serializer.Serialize(payload), DateTimeOffset.UtcNow, scopeId);
            await store.AppendIfNotExistsAsync(evt, ct);   // durable BEFORE ack
            _next++;
            return evt;
        }
        finally
        {
            _gate.Release();
        }
    }
}
