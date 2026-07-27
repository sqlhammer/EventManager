using EventManager.Api.Auth;
using EventManager.Api.Services;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>
/// Read/query API (U9, Epic 7 US-701..US-710). Nine GET endpoints over the existing read models,
/// governed by the three-tier access model resolved by <see cref="ReadAuthorizer"/>.
///
/// Two conventions differ from the write controllers and are deliberate:
///  • Insufficient tier returns 404, never 403 (BR-READ-20) — a 403 confirms the resource exists,
///    which is exactly the disclosure US-709 forbids.
///  • Tier is resolved before any data access, and the resolved tier — not the endpoint — selects
///    the response shape (BR-READ-7).
/// </summary>
[Route("api/events")]
[Authorize]
public sealed class EventReadController(
    ReadAuthorizer authorizer,
    ReadEtagProvider etags,
    EventQueryService events,
    DivisionQueryService divisions,
    WeighInPolicyQueryService policies,
    RegistrantQueryService registrants,
    OrganizerAccountQueryService accounts,
    CurrentUser currentUser,
    ILogger<EventReadController> logger) : ApiControllerBase
{
    // ---- 1. Event collection (no ETag — spans event scopes, BR-READ-25) ----

    [HttpGet]
    public async Task<IActionResult> ListEvents(CancellationToken ct)
    {
        var items = await events.ListAsync(currentUser.RequireAccountId(), ct);
        return Ok(items);
    }

    // ---- 2. Event single ----

    [HttpGet("{eventId:long}")]
    public async Task<IActionResult> GetEvent(long eventId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(GetEvent)) is { } denied) return denied;

        if (await ConditionalAsync("event", eventId, tier, ct) is { } notModified) return notModified;
        return Respond(await events.GetAsync(eventId, tier, ct), body => Ok(body));
    }

    // ---- 3-4. Divisions ----

    [HttpGet("{eventId:long}/divisions")]
    public async Task<IActionResult> ListDivisions(long eventId, [FromQuery] bool includeCompleted = false, CancellationToken ct = default)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(ListDivisions)) is { } denied) return denied;

        if (await ConditionalAsync("divisions", eventId, tier, ct, includeCompleted.ToString()) is { } notModified) return notModified;
        return Respond(await divisions.ListAsync(eventId, tier, includeCompleted, ct), body => Ok(body));
    }

    [HttpGet("{eventId:long}/divisions/{divisionId:long}")]
    public async Task<IActionResult> GetDivision(long eventId, long divisionId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(GetDivision)) is { } denied) return denied;

        if (await ConditionalAsync($"division:{divisionId}", eventId, tier, ct) is { } notModified) return notModified;
        return Respond(await divisions.GetAsync(eventId, divisionId, tier, ct), body => Ok(body));
    }

    // ---- 5. Weigh-in policy (single only — no collection form, BR-READ-12) ----

    [HttpGet("{eventId:long}/weigh-in-policy")]
    public async Task<IActionResult> GetWeighInPolicy(long eventId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(GetWeighInPolicy)) is { } denied) return denied;

        if (await ConditionalAsync("weigh-in-policy", eventId, tier, ct) is { } notModified) return notModified;
        return Respond(await policies.GetAsync(eventId, tier, ct), body => Ok(body));
    }

    // ---- 6-7. Registrants ----

    [HttpGet("{eventId:long}/registrants")]
    public async Task<IActionResult> ListRegistrants(long eventId, [FromQuery] bool includeWithdrawn = false, CancellationToken ct = default)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(ListRegistrants)) is { } denied) return denied;

        if (await ConditionalAsync("registrants", eventId, tier, ct, includeWithdrawn.ToString()) is { } notModified) return notModified;
        return Respond(await registrants.ListAsync(eventId, tier, includeWithdrawn, ct), body => Ok(body));
    }

    /// <summary>
    /// Registrant detail. **Deliberately issues no ETag** (BR-READ-26, U9-CON-2): the response
    /// carries athlete profile fields, and profile events are appended with the ATHLETE id as their
    /// scope rather than the event id, so the event watermark does not move when an athlete edits
    /// their weight. A conditional response here could return 304 with a stale weight.
    /// </summary>
    [HttpGet("{eventId:long}/registrants/{registrationId:long}")]
    public async Task<IActionResult> GetRegistrant(long eventId, long registrationId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(GetRegistrant)) is { } denied) return denied;

        return Respond(await registrants.GetAsync(eventId, registrationId, tier, caller, ct), body => Ok(body));
    }

    // ---- 8-9. Organizer accounts ----

    [HttpGet("{eventId:long}/accounts")]
    public async Task<IActionResult> ListAccounts(long eventId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(ListAccounts)) is { } denied) return denied;

        if (await ConditionalAsync("accounts", eventId, tier, ct) is { } notModified) return notModified;
        return Respond(await accounts.ListAsync(eventId, tier, ct), body => Ok(body));
    }

    [HttpGet("{eventId:long}/accounts/{accountId:long}")]
    public async Task<IActionResult> GetAccount(long eventId, long accountId, CancellationToken ct)
    {
        var caller = currentUser.RequireAccountId();
        var tier = await authorizer.ResolveAsync(caller, eventId, ct);
        if (Denied(tier, caller, eventId, nameof(GetAccount)) is { } denied) return denied;

        if (await ConditionalAsync($"account:{accountId}", eventId, tier, ct) is { } notModified) return notModified;
        return Respond(await accounts.GetAsync(eventId, accountId, tier, ct), body => Ok(body));
    }

    // ---- Shared helpers ----

    /// <summary>
    /// BR-READ-18/20/21: a caller with no tier gets exactly the same 404 as one asking for an event
    /// that does not exist. The denial is logged with actor, event, and endpoint — and no PII.
    /// </summary>
    private IActionResult? Denied(AccessTier tier, long caller, long eventId, string endpoint)
    {
        if (tier != AccessTier.None) return null;
        logger.LogInformation("Read denied: account {AccountId} has no tier on event {EventId} at {Endpoint}",
            caller, eventId, endpoint);
        return Problem([EventQueryService.NotFound()]);
    }

    /// <summary>
    /// Computes the ETag and short-circuits to 304 when it matches. On a hit no read-model table is
    /// touched — only the watermark lookup and the hash (U9-NFR-1).
    /// </summary>
    private async Task<IActionResult?> ConditionalAsync(
        string endpointIdentity, long eventId, AccessTier tier, CancellationToken ct, params string[] flags)
    {
        var watermark = await etags.WatermarkAsync(eventId, ct);
        var etag = etags.Build(endpointIdentity, eventId, watermark, tier, flags);
        Response.Headers.ETag = etag;

        if (ReadEtagProvider.Matches(Request.Headers.IfNoneMatch, etag)) return StatusCode(StatusCodes.Status304NotModified);
        return null;
    }
}
