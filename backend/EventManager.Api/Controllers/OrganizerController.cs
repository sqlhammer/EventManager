using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Organizer RBAC management (US-108/109). Full-Admin gating enforced in the service.</summary>
[Route("api/events/{eventId:long}/organizers")]
[Authorize]
public sealed class OrganizerController(OrganizerRoleService organizers, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(long eventId, AddOrganizerRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var caller = currentUser.RequireAccountId();
        if (req.AccountId is { } accountId)
            return Respond(await organizers.AddExistingAsync(caller, eventId, accountId, ct), id => Ok(new IdResponse(id)));
        return Respond(await organizers.InviteByEmailAsync(caller, eventId, req.Email!, ct));
    }

    [HttpPut("role")]
    public async Task<IActionResult> ChangeRole(long eventId, ChangeRoleRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        return Respond(await organizers.ChangeRoleAsync(currentUser.RequireAccountId(), eventId, req.TargetAccountId, req.NewRole, ct));
    }

    [HttpDelete("{targetAccountId:long}")]
    public async Task<IActionResult> Remove(long eventId, long targetAccountId, CancellationToken ct) =>
        Respond(await organizers.RemoveAsync(currentUser.RequireAccountId(), eventId, targetAccountId, ct));
}
