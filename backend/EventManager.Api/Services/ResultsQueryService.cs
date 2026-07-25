using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

public sealed record AthleteResult(long EventId, long DivisionId, int? Placement, int Wins, int Losses, string Status);
public sealed record ResultsResponse(long AthleteId, IReadOnlyList<AthleteResult> Results);

/// <summary>
/// US-603 registrant results & history. Reads the ResultsProjection (Q6=A) — empty/partial until real
/// event-day events are ingested. Object-level authz: only the owning/managing account may read.
/// </summary>
public sealed class ResultsQueryService(AppDbContext db)
{
    public async Task<ErrorOr<ResultsResponse>> GetForAthleteAsync(long callerAccountId, long athleteId, CancellationToken ct = default)
    {
        var owns = await db.AthleteProfileRows.AnyAsync(a => a.AthleteId == athleteId && a.OwnerAccountId == callerAccountId, ct);
        if (!owns) return Error.Forbidden("Results.Forbidden", "Not permitted to view these results.");

        var rows = await db.ResultRows.AsNoTracking().Where(r => r.AthleteId == athleteId).ToListAsync(ct);
        var results = rows.Select(r => new AthleteResult(r.EventId, r.DivisionId, r.Placement, r.Wins, r.Losses, r.Status)).ToList();
        return new ResultsResponse(athleteId, results);
    }
}
