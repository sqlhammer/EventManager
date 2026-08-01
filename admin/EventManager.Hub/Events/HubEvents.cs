using EventManager.Hub.Persistence;
using EventManager.Hub.Projections;
using EventManager.Sync;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Events;

/// <summary>Hub-authored device-lifecycle event vocabulary (US-303/305/508 — auditable events).</summary>
public static class HubEventTypes
{
    public const string DevicePaired = "DevicePaired";
    public const string DeviceRevoked = "DeviceRevoked";
    public const string DeviceRoleChanged = "DeviceRoleChanged";
}

public sealed record DevicePairedPayload(long DeviceId, long EventId, string RoleDescriptor, int WorkerId);
public sealed record DeviceRevokedPayload(long DeviceId, long EventId);
public sealed record DeviceRoleChangedPayload(long DeviceId, long EventId, string RoleDescriptor);

/// <summary>Single hub write path: mint id → contiguous hub sequence → append → project (mirrors U3).</summary>
public sealed class HubEventWriter(
    HubDbContext db, IIdGenerator ids, IEventSerializer ser, HubProjectionHost projections,
    EventManager.Hub.Resilience.ReplicationSignal? replication = null)
{
    /// <summary>Hub origin device id (the admin hub is a device; worker 0 within its own scope).</summary>
    public const long HubDeviceId = 1_000_000;

    private long _cachedNext = -1;

    public async Task<long> AppendAsync<T>(long scopeId, string eventType, T payload, CancellationToken ct = default)
    {
        var seq = await NextSequenceAsync(ct);
        var record = new HubEventRecord
        {
            EventId = ids.NextId(), DeviceId = HubDeviceId, SequenceNumber = seq, EventType = eventType,
            SchemaVersion = 1, Payload = ser.Serialize(payload).ToArray(), OccurredAt = DateTimeOffset.UtcNow, EventScopeId = scopeId,
        };
        db.Events.Add(record);
        projections.Dispatch(record);

        // U10 (AD-Q5=C): nudge replication. Non-blocking and safe to drop — the signal carries no
        // data, and the drain timer is the backstop, so an append is never delayed by the cloud.
        replication?.Signal();

        return record.EventId;
    }

    private async Task<long> NextSequenceAsync(CancellationToken ct)
    {
        if (_cachedNext < 0)
        {
            var max = await db.Events.Where(e => e.DeviceId == HubDeviceId)
                .Select(e => (long?)e.SequenceNumber).MaxAsync(ct) ?? 0;
            _cachedNext = max + 1;
        }
        return _cachedNext++;
    }
}
