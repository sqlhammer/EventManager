using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Organizer account reads (US-708). Organizer tier only, and scoped strictly to accounts holding a
/// role on the path event (C2=B).
///
/// The original request was "any account by id" (Q5=C). That was rejected at requirements as a
/// blocking SECURITY-08 finding: it would let any organizer probe arbitrary account ids and learn
/// which accounts exist along with their identity data. Reading only the event's own roster keeps
/// object-level authorization intact.
///
/// BR-READ-17: there is deliberately no soft-deleted-account filter or inclusion flag. Account
/// deletion appends OrganizerRemoved for every role held and the projection deletes the
/// OrganizerRow, so a deleted account can never reach this query in the first place.
/// </summary>
public sealed class OrganizerAccountQueryService(AppDbContext db)
{
    public async Task<ErrorOr<IReadOnlyList<OrganizerAccountResponse>>> ListAsync(
        long eventId, AccessTier tier, CancellationToken ct = default)
    {
        if (tier < AccessTier.Organizer) return EventQueryService.NotFound();

        var rows = await db.OrganizerRows.AsNoTracking().Where(o => o.EventId == eventId).ToListAsync(ct);
        var accountIds = rows.Select(o => o.AccountId).ToList();
        var emails = await EmailsForAsync(accountIds, ct);

        var result = new List<OrganizerAccountResponse>(rows.Count);
        foreach (var row in rows)
        {
            emails.TryGetValue(row.AccountId, out var email);
            result.Add(new OrganizerAccountResponse(row.AccountId, email ?? "", row.Role));
        }
        IReadOnlyList<OrganizerAccountResponse> response = result;
        return ErrorOrFactory.From(response);
    }

    public async Task<ErrorOr<OrganizerAccountResponse>> GetAsync(
        long eventId, long accountId, AccessTier tier, CancellationToken ct = default)
    {
        if (tier < AccessTier.Organizer) return EventQueryService.NotFound();

        var row = await db.OrganizerRows.AsNoTracking()
            .FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == accountId, ct);
        // An account that exists but holds no role on this event is a 404 — this endpoint never
        // confirms the existence of unrelated accounts (BR-READ-10).
        if (row is null) return EventQueryService.NotFound();

        var emails = await EmailsForAsync([accountId], ct);
        emails.TryGetValue(accountId, out var email);
        return new OrganizerAccountResponse(row.AccountId, email ?? "", row.Role);
    }

    /// <summary>Joins the identity plane for contact email only. No credential, MFA, or session
    /// material is ever read here (BR-READ-11).</summary>
    private async Task<Dictionary<long, string>> EmailsForAsync(IReadOnlyList<long> accountIds, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => accountIds.Contains(u.AccountId))
            .Select(u => new { u.AccountId, u.Email })
            .ToListAsync(ct);

        var map = new Dictionary<long, string>();
        foreach (var user in users) map[user.AccountId] = user.Email ?? "";
        return map;
    }
}
