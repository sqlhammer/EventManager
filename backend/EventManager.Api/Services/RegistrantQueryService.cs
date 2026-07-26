using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Registrant reads (US-707). The list is Organizer-only and minimal; detail adds the profile
/// fields organizers need for weigh-in checks and is readable by an Organizer for any registration,
/// or by a Registrant for their own records only (BR-READ-8/9).
/// </summary>
public sealed class RegistrantQueryService(AppDbContext db)
{
    public async Task<ErrorOr<IReadOnlyList<RegistrantListItemResponse>>> ListAsync(
        long eventId, AccessTier tier, bool includeWithdrawn, CancellationToken ct = default)
    {
        // BR-READ-8: the roster is Organizer-only. Lower tiers get the same 404 as a stranger.
        if (tier < AccessTier.Organizer) return EventQueryService.NotFound();

        var query = db.RegistrationRows.AsNoTracking().Where(r => r.EventId == eventId);
        if (!includeWithdrawn) query = query.Where(r => !r.Withdrawn);

        // BR-READ-16 (Q2=A): registrations whose managing account has been deleted are returned
        // normally. Account deletion anonymizes the identity record only — it never withdraws the
        // athlete's entry, and an athlete registered by a since-deleted parent or coach account is
        // still competing and must appear on the roster.
        var rows = await query.ToListAsync(ct);
        IReadOnlyList<RegistrantListItemResponse> result = rows.Select(r => new RegistrantListItemResponse(
            r.RegistrationId, r.AthleteId, r.AthleteName, r.Academy, ParseDivisionIds(r.DivisionIdsCsv),
            r.PaymentStatus, r.HasAssignmentMismatch, r.Withdrawn)).ToList();
        return ErrorOrFactory.From(result);
    }

    public async Task<ErrorOr<RegistrantDetailResponse>> GetAsync(
        long eventId, long registrationId, AccessTier tier, long callerAccountId, CancellationToken ct = default)
    {
        if (tier == AccessTier.None) return EventQueryService.NotFound();

        // BR-READ-19: the registration must belong to the path event.
        var row = await db.RegistrationRows.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId && r.EventId == eventId, ct);
        if (row is null) return EventQueryService.NotFound();

        // BR-READ-9: an Organizer reads any registration; a Registrant only their own. Anything
        // else is a 404, so a registrant cannot discover that another registration exists.
        if (tier < AccessTier.Organizer && row.ManagedByAccountId != callerAccountId)
            return EventQueryService.NotFound();

        var profile = await db.AthleteProfileRows.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AthleteId == row.AthleteId, ct);

        DateOnly? dob = null;
        double? weight = null;
        int? rank = null;
        string? gender = null;
        if (profile is not null)
        {
            dob = profile.DateOfBirth;
            weight = profile.Weight;
            rank = profile.Rank;
            gender = profile.Gender;
        }

        return new RegistrantDetailResponse(
            row.RegistrationId, row.AthleteId, row.AthleteName, row.Academy, ParseDivisionIds(row.DivisionIdsCsv),
            row.PaymentStatus, row.HasAssignmentMismatch, row.MismatchReasons, row.Withdrawn,
            dob, weight, rank, gender);
    }

    private static IReadOnlyList<long> ParseDivisionIds(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var ids = new List<long>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part.Trim(), out var id)) ids.Add(id);
        }
        return ids;
    }
}
