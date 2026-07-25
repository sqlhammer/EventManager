using EventManager.Api.Auth;
using EventManager.Api.Events;
using EventManager.Api.Persistence;
using EventManager.Domain;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// S-2 organizer RBAC management (US-108/109). Only Full Admin may add/invite (BR-RBAC-1) or perform
/// Full-Admin-only role changes (BR-RBAC-2, via U1 policy); last-admin guard prevents lockout (BR-RBAC-3).
/// New organizers default to Co-Organizer (BR-RBAC-4).
/// </summary>
public sealed class OrganizerRoleService(
    AppDbContext db, EventWriter writer, EventManager.Sync.IIdGenerator ids, EventAuthorizer authz, IEmailSender email)
{
    /// <summary>Add an existing organizer account directly (D-21). Full-Admin only.</summary>
    public async Task<ErrorOr<long>> AddExistingAsync(long callerAccountId, long eventId, long inviteeAccountId, CancellationToken ct = default)
    {
        if (!await authz.IsFullAdminAsync(callerAccountId, eventId, ct))
            return Error.Forbidden("Organizer.Forbidden", "Only a Full Admin can add organizers.");
        if (await db.OrganizerRows.AnyAsync(o => o.EventId == eventId && o.AccountId == inviteeAccountId, ct))
            return Error.Conflict("Organizer.Exists", "Account is already an organizer on this event.");

        var id = ids.NextId();
        await writer.AppendAsync(eventId, EventTypes.OrganizerAssigned,
            new OrganizerAssignedPayload(id, eventId, inviteeAccountId, nameof(OrganizerRole.CoOrganizer)), ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Invite a co-organizer by email (D-21). Records an invite token to the outbox stub (Q5).</summary>
    public async Task<ErrorOr<Success>> InviteByEmailAsync(long callerAccountId, long eventId, string inviteeEmail, CancellationToken ct = default)
    {
        if (!await authz.IsFullAdminAsync(callerAccountId, eventId, ct))
            return Error.Forbidden("Organizer.Forbidden", "Only a Full Admin can invite organizers.");
        var token = Guid.NewGuid().ToString("N");
        await email.SendOrganizerInviteAsync(inviteeEmail, token, ct);   // acceptance completes the assignment later
        return Result.Success;
    }

    /// <summary>Elevate/demote (US-109). Full-Admin-only actions gated by the U1 policy + last-admin guard.</summary>
    public async Task<ErrorOr<Success>> ChangeRoleAsync(long callerAccountId, long eventId, long targetAccountId, string newRole, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrganizerRole>(newRole, out var role))
            return Error.Validation("Organizer.Role", "Unknown role.");

        var action = role == OrganizerRole.FullAdmin ? OrganizerAction.TransferFullAdmin : OrganizerAction.DemoteOrganizer;
        if (!await authz.IsPermittedAsync(callerAccountId, eventId, action, ct))
            return Error.Forbidden("Organizer.Forbidden", "Full-Admin-only action.");

        var target = await db.OrganizerRows.FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == targetAccountId, ct);
        if (target is null) return Error.NotFound("Organizer.NotFound", "Target is not an organizer on this event.");

        // Last-admin guard (BR-RBAC-3): don't demote the last Full Admin.
        if (role == OrganizerRole.CoOrganizer && target.Role == nameof(OrganizerRole.FullAdmin))
        {
            var admins = await db.OrganizerRows.CountAsync(o => o.EventId == eventId && o.Role == nameof(OrganizerRole.FullAdmin), ct);
            if (admins <= 1) return Error.Conflict("Organizer.LastAdmin", "Cannot demote the last Full Admin.");
        }

        await writer.AppendAsync(eventId, EventTypes.OrganizerRoleChanged,
            new OrganizerRoleChangedPayload(target.Id, eventId, targetAccountId, newRole), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    /// <summary>Remove an organizer (US-109). Full-Admin-only; cannot remove the last Full Admin.</summary>
    public async Task<ErrorOr<Success>> RemoveAsync(long callerAccountId, long eventId, long targetAccountId, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(callerAccountId, eventId, OrganizerAction.RemoveOrganizer, ct))
            return Error.Forbidden("Organizer.Forbidden", "Full-Admin-only action.");
        var target = await db.OrganizerRows.FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == targetAccountId, ct);
        if (target is null) return Error.NotFound("Organizer.NotFound", "Target is not an organizer on this event.");
        if (target.Role == nameof(OrganizerRole.FullAdmin))
        {
            var admins = await db.OrganizerRows.CountAsync(o => o.EventId == eventId && o.Role == nameof(OrganizerRole.FullAdmin), ct);
            if (admins <= 1) return Error.Conflict("Organizer.LastAdmin", "Cannot remove the last Full Admin.");
        }
        await writer.AppendAsync(eventId, EventTypes.OrganizerRemoved,
            new OrganizerRemovedPayload(target.Id, eventId, targetAccountId), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }
}
