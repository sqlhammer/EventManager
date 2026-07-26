using EventManager.Api.Persistence;
using EventManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Deletion pre-checks for self-service account removal (US-110). Deny-safe counterpart to the
/// organizer last-admin guard (BR-RBAC-3): an account cannot be deleted while it is the SOLE Full
/// Admin of any event, otherwise that event would be left with no one able to administer it.
/// DB-only (no identity plane) so it is independently unit-testable.
/// </summary>
public sealed class AccountDeletionGuard(AppDbContext db)
{
    /// <summary>
    /// Events where <paramref name="accountId"/> is a Full Admin AND is the only Full Admin.
    /// An empty list means deletion is not blocked.
    /// </summary>
    public async Task<IReadOnlyList<long>> SoleFullAdminEventsAsync(long accountId, CancellationToken ct = default)
    {
        var fullAdmin = nameof(OrganizerRole.FullAdmin);

        var adminEventIds = await db.OrganizerRows.AsNoTracking()
            .Where(o => o.AccountId == accountId && o.Role == fullAdmin)
            .Select(o => o.EventId)
            .ToListAsync(ct);

        var blocked = new List<long>();
        foreach (var eventId in adminEventIds)
        {
            var admins = await db.OrganizerRows.AsNoTracking()
                .CountAsync(o => o.EventId == eventId && o.Role == fullAdmin, ct);
            if (admins <= 1) blocked.Add(eventId);
        }
        return blocked;
    }

    /// <summary>Every organizer role assignment held by the account, across all events (to be detached on delete).</summary>
    public async Task<IReadOnlyList<OrganizerRow>> OrganizerRolesAsync(long accountId, CancellationToken ct = default) =>
        await db.OrganizerRows.AsNoTracking().Where(o => o.AccountId == accountId).ToListAsync(ct);
}
