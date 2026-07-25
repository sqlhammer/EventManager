using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Sync;

namespace EventManager.Hub.Projections;

/// <summary>
/// Synchronous inline hub projection host. Folds device-lifecycle events into the <see cref="DeviceRecord"/>
/// read model. Unknown types (competition events applied by U4b, or replicated spoke events) are ignored
/// here — U4b owns bracket/scoring projections.
/// </summary>
public sealed class HubProjectionHost(HubDbContext db, IEventSerializer ser)
{
    public void Dispatch(HubEventRecord r)
    {
        switch (r.EventType)
        {
            case HubEventTypes.DevicePaired:
            {
                var p = ser.Deserialize<DevicePairedPayload>(r.Payload);
                db.Devices.Add(new DeviceRecord { DeviceId = p.DeviceId, EventId = p.EventId, RoleDescriptor = p.RoleDescriptor, WorkerId = p.WorkerId });
                break;
            }
            case HubEventTypes.DeviceRevoked:
            {
                var p = ser.Deserialize<DeviceRevokedPayload>(r.Payload);
                var d = db.Devices.Find(p.DeviceId); if (d is not null) d.Revoked = true;
                break;
            }
            case HubEventTypes.DeviceRoleChanged:
            {
                var p = ser.Deserialize<DeviceRoleChangedPayload>(r.Payload);
                var d = db.Devices.Find(p.DeviceId); if (d is not null) d.RoleDescriptor = p.RoleDescriptor;
                break;
            }
            default: break; // ignore non-device events (U4b / replicated spoke events)
        }
    }
}
