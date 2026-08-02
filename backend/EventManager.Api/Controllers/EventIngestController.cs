using EventManager.Api.Auth;
using EventManager.Api.Services;
using EventManager.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventManager.Api.Controllers;

/// <summary>
/// Replication ingest (US-504). Accepts BOTH principal types (U10, AD-Q3=A): the hub-credential
/// scheme for hubs and the existing JWT scheme for organizer accounts, so account-based ingest keeps
/// working. Idempotent and sequence-ordered.
///
/// Hardening (U10-FR-15): the "ingest" rate-limit policy and an explicit request-body cap. The body
/// cap is deliberately twice the hub's own 4 MB batch cap, so a conforming hub can never trip it and
/// a 413 unambiguously means a non-conforming caller.
/// </summary>
[Route("api/ingest")]
[Authorize(AuthenticationSchemes = $"{HubCredentialDefaults.Scheme},{JwtBearerDefaults.AuthenticationScheme}")]
[EnableRateLimiting(IngestPolicy.Name)]
public sealed class EventIngestController(IngestService ingest, CurrentCaller caller) : ApiControllerBase
{
    [HttpPost("batch")]
    [RequestSizeLimit(IngestPolicy.MaxRequestBytes)]
    public async Task<IActionResult> IngestBatch(ReplicationBatchDto batch, CancellationToken ct) =>
        Respond(await ingest.IngestAsync(caller.Require(), batch, ct), ack => Ok(ack));

    /// <summary>Cloud cursors for the caller's event scope — lets a restarted hub resume (US-805).</summary>
    [HttpGet("high-water-marks")]
    public async Task<IActionResult> HighWaterMarks([FromQuery] long? eventId, CancellationToken ct)
    {
        var principal = caller.Require();
        var scope = ResolveScope(principal, eventId);
        if (scope is null)
            return Problem(detail: "eventId is required for account callers.", statusCode: StatusCodes.Status400BadRequest,
                title: "Ingest.EventId");

        return Respond(await ingest.HighWaterMarksAsync(principal, scope.Value, ct), hwm => Ok(hwm));
    }

    /// <summary>A hub credential is already bound to one event, so it need not name it.</summary>
    private static long? ResolveScope(IngestCaller principal, long? requested)
    {
        if (principal is IngestCaller.Hub hub) return hub.EventScopeId;
        return requested;
    }
}
