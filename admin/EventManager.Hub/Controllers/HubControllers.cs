using EventManager.Contracts;
using EventManager.Hub.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Hub.Controllers;

public sealed record IssueTokenRequest(long EventId, string RoleDescriptor);
public sealed record ChangeDeviceRoleRequest(string RoleDescriptor);

/// <summary>Pairing endpoints (US-303/304). Token issue (organizer) + redeem (spoke, anonymous over WSS).</summary>
[Route("api/pairing")]
public sealed class PairingController(PairingService pairing) : HubControllerBase
{
    [HttpPost("tokens")]
    public async Task<IActionResult> IssueToken(IssueTokenRequest req, CancellationToken ct) =>
        Ok(await pairing.IssueTokenAsync(req.EventId, req.RoleDescriptor, ct));

    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem(PairingRequestDto req, CancellationToken ct) =>
        Respond(await pairing.RedeemAsync(req, ct), r => Ok(r));
}

/// <summary>Spoke → hub sync intake (US-407). Device id from the pinned credential header.</summary>
[Route("api/sync")]
public sealed class SyncController(SyncIntakeService intake) : HubControllerBase
{
    [HttpPost("batch")]
    public async Task<IActionResult> Intake([FromHeader(Name = "X-Device-Id")] long deviceId, ReplicationBatchDto batch, CancellationToken ct) =>
        Respond(await intake.IntakeAsync(deviceId, batch, ct), ack => Ok(ack));
}

/// <summary>Device management (US-305/508). List / reassign / revoke.</summary>
[Route("api/events/{eventId:long}/devices")]
public sealed class DeviceController(DeviceRegistry devices) : HubControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long eventId, CancellationToken ct) => Ok(await devices.ListAsync(eventId, ct));

    [HttpDelete("{deviceId:long}")]
    public async Task<IActionResult> Revoke(long eventId, long deviceId, CancellationToken ct) =>
        Respond(await devices.RevokeAsync(deviceId, ct));

    [HttpPut("{deviceId:long}/role")]
    public async Task<IActionResult> ChangeRole(long eventId, long deviceId, ChangeDeviceRoleRequest req, CancellationToken ct) =>
        Respond(await devices.ChangeRoleAsync(deviceId, req.RoleDescriptor, ct));
}
