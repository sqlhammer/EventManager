using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Event reads (US-704). Collection spans all three tiers; single-event shape is chosen by the
/// caller's resolved tier (BR-READ-7).
///
/// Insufficient tier returns <see cref="Error.NotFound"/>, never Forbidden — BR-READ-20 forbids 403
/// on read endpoints because a 403 confirms the resource exists.
/// </summary>
public sealed class EventQueryService(AppDbContext db, ReadAuthorizer authorizer)
{
    public async Task<IReadOnlyList<EventListItemResponse>> ListAsync(long callerAccountId, CancellationToken ct = default)
    {
        var tiers = await authorizer.ResolveAllAsync(callerAccountId, ct);
        if (tiers.Count == 0) return [];

        var roles = await authorizer.OrganizerRolesAsync(callerAccountId, ct);
        var ids = tiers.Keys.ToList();
        var rows = await db.EventRows.AsNoTracking().Where(e => ids.Contains(e.EventId)).ToListAsync(ct);

        var items = new List<EventListItemResponse>(rows.Count);
        foreach (var row in rows)
        {
            roles.TryGetValue(row.EventId, out var role);
            items.Add(new EventListItemResponse(
                row.EventId, row.Name, row.Venue, row.Date, row.RegistrationStart, row.RegistrationEnd,
                row.EntryFee, row.RegistrationStatus.ToString(), tiers[row.EventId].ToString(), role));
        }
        return items;
    }

    /// <summary>Returns the summary shape at Public and the detail shape at Registrant/Organizer.</summary>
    public async Task<ErrorOr<object>> GetAsync(long eventId, AccessTier tier, CancellationToken ct = default)
    {
        if (tier == AccessTier.None) return NotFound();

        var row = await db.EventRows.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (row is null) return NotFound();

        if (tier == AccessTier.Public)
        {
            return new EventSummaryResponse(row.EventId, row.Name, row.Venue, row.Date,
                row.RegistrationStart, row.RegistrationEnd, row.EntryFee, row.RegistrationStatus.ToString());
        }

        var policy = new WeighInPolicyResponse(row.WeighInPolicyMode, ToleranceFor(row));
        return new EventDetailResponse(row.EventId, row.Name, row.Venue, row.Date,
            row.RegistrationStart, row.RegistrationEnd, row.EntryFee, row.RegistrationStatus.ToString(),
            row.CardEnabled, row.CheckInStarted, policy, row.CreatedByAccountId);
    }

    /// <summary>BR-READ-12: tolerance is meaningful only under the Tolerance policy mode.</summary>
    internal static double? ToleranceFor(EventRow row)
    {
        if (row.WeighInPolicyMode == nameof(EventManager.Domain.WeighInPolicyMode.Tolerance)) return row.WeighInTolerancePercent;
        return null;
    }

    /// <summary>BR-READ-18: one identical shape for "does not exist" and "you hold no tier".</summary>
    internal static Error NotFound() => Error.NotFound("Read.NotFound", "Not found.");
}
