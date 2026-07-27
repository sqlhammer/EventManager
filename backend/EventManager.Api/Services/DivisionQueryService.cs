using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using EventManager.Domain;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>Division reads (US-705). Public tier and above.</summary>
public sealed class DivisionQueryService(AppDbContext db)
{
    public async Task<ErrorOr<IReadOnlyList<DivisionResponse>>> ListAsync(
        long eventId, AccessTier tier, bool includeCompleted, CancellationToken ct = default)
    {
        if (tier == AccessTier.None) return EventQueryService.NotFound();

        var query = db.DivisionRows.AsNoTracking().Where(d => d.EventId == eventId);
        if (!includeCompleted)
        {
            // BR-READ-15: completed divisions are excluded unless explicitly requested.
            var complete = nameof(DivisionStatus.Complete);
            query = query.Where(d => d.Status != complete);
        }

        var rows = await query.ToListAsync(ct);
        IReadOnlyList<DivisionResponse> result = rows.Select(Map).ToList();
        return ErrorOrFactory.From(result);
    }

    public async Task<ErrorOr<DivisionResponse>> GetAsync(long eventId, long divisionId, AccessTier tier, CancellationToken ct = default)
    {
        if (tier == AccessTier.None) return EventQueryService.NotFound();

        // BR-READ-19: the id must belong to the path event. A division that exists elsewhere is a
        // 404 here, so cross-event probing reveals nothing.
        var row = await db.DivisionRows.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DivisionId == divisionId && d.EventId == eventId, ct);
        if (row is null) return EventQueryService.NotFound();
        return Map(row);
    }

    private static DivisionResponse Map(DivisionRow d) => new(
        d.DivisionId, d.EventId, d.WeightLower, d.WeightUpper, d.MinRank, d.MaxRank,
        d.MinAge, d.MaxAge, d.Gender, d.Format, d.Status);
}
