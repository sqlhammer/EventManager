using EventManager.Contracts;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Sync;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Services;

/// <summary>
/// Device management (US-305/508). List, reassign role, revoke. Revocation frees the worker id and
/// flips the credential so it is rejected on the device's next contact; each change is an event.
/// </summary>
public sealed class DeviceRegistry(HubDbContext db, HubEventWriter writer, IWorkerIdRegistry workers, IHubPush push)
{
    public async Task<IReadOnlyList<DeviceRecord>> ListAsync(long eventId, CancellationToken ct = default) =>
        await db.Devices.AsNoTracking().Where(d => d.EventId == eventId).ToListAsync(ct);

    /// <summary>True if the device is known and not revoked (enforced on every spoke message, US-508).</summary>
    public async Task<bool> IsActiveAsync(long deviceId, CancellationToken ct = default) =>
        await db.Devices.AsNoTracking().AnyAsync(d => d.DeviceId == deviceId && !d.Revoked, ct);

    public async Task<ErrorOr<Success>> RevokeAsync(long deviceId, CancellationToken ct = default)
    {
        var device = await db.Devices.FindAsync([deviceId], ct);
        if (device is null) return Error.NotFound("Device.NotFound", "Device not found.");
        if (device.Revoked) return Result.Success;

        await writer.AppendAsync(device.EventId, HubEventTypes.DeviceRevoked, new DeviceRevokedPayload(deviceId, device.EventId), ct);
        workers.Release(deviceId);
        await db.SaveChangesAsync(ct);
        await push.PushAsync(new HubPushMessageDto(PushType.DeviceRevoked, deviceId.ToString()), ct); // notify LAN
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> ChangeRoleAsync(long deviceId, string roleDescriptor, CancellationToken ct = default)
    {
        var device = await db.Devices.FindAsync([deviceId], ct);
        if (device is null) return Error.NotFound("Device.NotFound", "Device not found.");
        if (device.Revoked) return Error.Conflict("Device.Revoked", "Cannot reassign a revoked device.");

        await writer.AppendAsync(device.EventId, HubEventTypes.DeviceRoleChanged, new DeviceRoleChangedPayload(deviceId, device.EventId, roleDescriptor), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }
}
