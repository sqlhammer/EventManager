using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Weigh-in policy read (US-706). Public tier and above — a prospective registrant needs the policy
/// to judge the risk of competing at their current weight before entering (U9-CON-4, Q8=A).
///
/// There is exactly one policy per event and no collection form: the collection endpoint was
/// removed from scope, because a one-item collection carries no information the single endpoint
/// does not (BR-READ-12).
/// </summary>
public sealed class WeighInPolicyQueryService(AppDbContext db)
{
    public async Task<ErrorOr<WeighInPolicyResponse>> GetAsync(long eventId, AccessTier tier, CancellationToken ct = default)
    {
        if (tier == AccessTier.None) return EventQueryService.NotFound();

        var row = await db.EventRows.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (row is null) return EventQueryService.NotFound();

        return new WeighInPolicyResponse(row.WeighInPolicyMode, EventQueryService.ToleranceFor(row));
    }
}
