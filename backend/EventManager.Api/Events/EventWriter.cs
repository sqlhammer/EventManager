using EventManager.Api.Persistence;
using EventManager.Api.Projections;
using EventManager.Sync;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Events;

/// <summary>
/// The single domain write path (BR-X-1): mint id → assign contiguous cloud sequence → append to the
/// event log → dispatch projections synchronously (Q2=A/PP-2). Callers wrap related appends in one
/// transaction for atomic multi-event commits (RP-4). SaveChanges is the caller's responsibility so
/// several appends + the projection updates commit together.
/// </summary>
public sealed class EventWriter(AppDbContext db, IIdGenerator ids, IEventSerializer serializer, CloudProjectionHost projections)
{
    /// <summary>Cloud origin device id (worker 0 reserved for the cloud, per WorkerIdRegistry).</summary>
    public const long CloudDeviceId = 0;

    /// <summary>Append a domain event and fold it into read models. Returns the new EventId.
    /// Does NOT call SaveChanges — the caller commits the surrounding transaction.</summary>
    public async Task<long> AppendAsync<T>(long scopeId, string eventType, T payload, CancellationToken ct = default)
    {
        var seq = await NextCloudSequenceAsync(ct);
        var record = new EventRecord
        {
            EventId = ids.NextId(),
            DeviceId = CloudDeviceId,
            SequenceNumber = seq,
            EventType = eventType,
            SchemaVersion = 1,
            Payload = serializer.Serialize(payload).ToArray(),
            OccurredAt = DateTimeOffset.UtcNow,
            EventScopeId = scopeId,
        };
        db.Events.Add(record);
        projections.Dispatch(record);   // synchronous inline projection (same DbContext)
        return record.EventId;
    }

    private long _cachedNext = -1;

    private async Task<long> NextCloudSequenceAsync(CancellationToken ct)
    {
        if (_cachedNext < 0)
        {
            var max = await db.Events
                .Where(e => e.DeviceId == CloudDeviceId)
                .Select(e => (long?)e.SequenceNumber)
                .MaxAsync(ct) ?? 0;
            _cachedNext = max + 1;
        }
        return _cachedNext++;   // contiguous within a request (single-writer, Q1=A)
    }
}
