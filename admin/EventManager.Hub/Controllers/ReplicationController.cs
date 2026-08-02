using EventManager.Hub.Resilience;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Hub.Controllers;

public sealed record InstallCredentialRequest(string Key, string CloudBaseUrl);

/// <summary>
/// The hub's replication surface (US-802, US-806, US-807) — and the resolution of U10-CON-5.
///
/// The cloud could issue a credential and the hub could store one, but nothing connected them: the
/// hub has no UI, because the MAUI shell is still a deferred seam. This endpoint is that connection.
/// An organizer pastes the key here (Postman or curl today, a MAUI screen later).
///
/// No response on this controller ever echoes the credential (BR-REPL-24).
/// </summary>
[Route("api/replication")]
public sealed class ReplicationController(
    HubCredentialStore credentials, ReplicationClient replication, ReplicationStatus status) : HubControllerBase
{
    /// <summary>
    /// Install a credential. Refuses when one is already present (FD-Q8=B) — the cost is a two-step
    /// rotation, the benefit is that a working credential cannot be destroyed by a careless paste.
    /// </summary>
    [HttpPost("credential")]
    public async Task<IActionResult> Install(InstallCredentialRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Key))
            return Problem(detail: "A credential key is required.", statusCode: StatusCodes.Status400BadRequest,
                title: "Replication.Key");

        var outcome = await credentials.InstallAsync(req.Key, req.CloudBaseUrl, ct);

        if (outcome == CredentialInstallOutcome.Installed)
        {
            status.CredentialInstalled = true;
            return Ok(new { installed = true });
        }

        var detail = outcome switch
        {
            CredentialInstallOutcome.RefusedSlotOccupied =>
                "A credential is already installed. Clear it first (DELETE /api/replication/credential), then install the new one.",
            CredentialInstallOutcome.RefusedInsecureUrl =>
                "The cloud address must use HTTPS.",
            _ => "The cloud address is not a valid absolute URL.",
        };
        var code = outcome switch
        {
            CredentialInstallOutcome.RefusedSlotOccupied => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Problem(detail: detail, statusCode: code, title: "Replication.Install");
    }

    /// <summary>Explicit clear — the other half of FD-Q8=B's refuse-rather-than-overwrite rule.</summary>
    [HttpDelete("credential")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await credentials.ClearAsync(ct);
        status.CredentialInstalled = false;
        return Ok(new { installed = false });
    }

    /// <summary>
    /// Live replication status (US-806). Computed on demand rather than served from cache, because it
    /// is read by a human, rarely, and usually because something looks wrong (ND-Q6=C).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await replication.ComputeStatusAsync(ct));

    /// <summary>
    /// Close out the event (US-807): drive replication to completion within the configured window,
    /// then report whether the cloud holds everything. Always returns — an answer of "incomplete" is
    /// more useful at a venue than a call that never comes back.
    /// </summary>
    [HttpPost("close-out")]
    public async Task<IActionResult> CloseOut(CancellationToken ct)
    {
        if (!await credentials.ExistsAsync(ct))
            return Problem(detail: "No cloud credential is installed, so completeness cannot be verified. "
                                   + "Install one and retry.",
                statusCode: StatusCodes.Status409Conflict, title: "Replication.NoCredential");

        var report = await replication.FlushForCloseOutAsync(ct);
        return Ok(new
        {
            fullyReplicated = report.IsComplete,
            localEvents = report.LocalEventCount,
            replicatedEvents = report.ReplicatedEventCount,
            outstanding = report.LocalEventCount - report.ReplicatedEventCount,
        });
    }
}
