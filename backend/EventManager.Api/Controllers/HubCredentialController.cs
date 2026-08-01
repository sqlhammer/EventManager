using EventManager.Api.Auth;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

public sealed record IssueHubCredentialRequest(string Label);

/// <summary>Issue response. The only shape that ever carries a key, and only once (BR-REPL-2).</summary>
public sealed record IssueHubCredentialResponse(
    long CredentialId, string Key, long EventId, DateTimeOffset ExpiresAt, string Warning);

/// <summary>
/// Organizer-facing hub-credential management (US-801, US-808). Authenticated with the EXISTING JWT
/// scheme — the caller here is a person. The hub-credential scheme is for hubs, on the ingest routes.
/// </summary>
[Route("api/events/{eventId:long}/hub-credentials")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class HubCredentialController(HubCredentialService credentials, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Issue(long eventId, IssueHubCredentialRequest req, CancellationToken ct) =>
        Respond(await credentials.IssueAsync(currentUser.RequireAccountId(), eventId, req.Label, ct),
            issued => Ok(new IssueHubCredentialResponse(
                issued.CredentialId, issued.Key, issued.EventScopeId, issued.ExpiresAt,
                "This key is shown once and cannot be retrieved again. Install it on the hub now.")));

    [HttpGet]
    public async Task<IActionResult> List(long eventId, CancellationToken ct) =>
        Respond(await credentials.ListAsync(currentUser.RequireAccountId(), eventId, ct), summaries => Ok(summaries));

    [HttpDelete("{credentialId:long}")]
    public async Task<IActionResult> Revoke(long eventId, long credentialId, CancellationToken ct) =>
        Respond(await credentials.RevokeAsync(currentUser.RequireAccountId(), eventId, credentialId, ct));
}
