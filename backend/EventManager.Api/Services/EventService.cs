using EventManager.Api.Auth;
using EventManager.Api.Events;
using EventManager.Api.Persistence;
using EventManager.Domain;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

public sealed record CreateEventInput(string Name, string Venue, DateOnly Date, DateOnly RegStart,
    DateOnly RegEnd, decimal EntryFee, string WeighInMode, double? TolerancePercent);

public sealed record ConfigureDivisionInput(long EventId, double? WeightLower, double WeightUpper,
    int MinRank, int MaxRank, int MinAge, int MaxAge, string Gender, string Format);

/// <summary>
/// S-1/S-2 event & division setup (US-104/105/106/107). Every mutation is an event (Q4=A, BR-X-1);
/// read models fold synchronously. Creator becomes Full Admin atomically (BR-EVT-2, D-20).
/// </summary>
public sealed class EventService(AppDbContext db, EventWriter writer, EventManager.Sync.IIdGenerator ids, EventAuthorizer authz)
{
    public async Task<ErrorOr<long>> CreateEventAsync(long creatorAccountId, CreateEventInput input, CancellationToken ct = default)
    {
        if (input.RegStart > input.RegEnd) return Error.Validation("Event.Dates", "Registration window start must be on/before end.");
        if (input.EntryFee < 0) return Error.Validation("Event.Fee", "Entry fee must be non-negative.");
        if (input.WeighInMode == nameof(WeighInPolicyMode.Tolerance) && input.TolerancePercent is null)
            return Error.Validation("Event.WeighIn", "Tolerance policy requires a percentage.");

        var eventId = ids.NextId();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await writer.AppendAsync(eventId, EventTypes.EventCreated, new EventCreatedPayload(
            eventId, input.Name, input.Venue, input.Date, input.RegStart, input.RegEnd, input.EntryFee,
            creatorAccountId, input.WeighInMode, input.TolerancePercent), ct);

        // Creator → Full Admin on the new event (BR-EVT-2, D-20)
        await writer.AppendAsync(eventId, EventTypes.OrganizerAssigned,
            new OrganizerAssignedPayload(ids.NextId(), eventId, creatorAccountId, nameof(OrganizerRole.FullAdmin)), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return eventId;
    }

    public async Task<ErrorOr<Success>> EditEventAsync(long accountId, long eventId, string name, string venue,
        DateOnly date, DateOnly regStart, DateOnly regEnd, decimal entryFee, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(accountId, eventId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("Event.Forbidden", "Not an organizer on this event.");
        var row = await db.EventRows.FindAsync([eventId], ct);
        if (row is null) return Error.NotFound("Event.NotFound", "Event not found.");
        if (regStart > regEnd) return Error.Validation("Event.Dates", "Registration window start must be on/before end.");
        if (entryFee < 0) return Error.Validation("Event.Fee", "Entry fee must be non-negative.");
        // BR-EVT-3: after Open, edits are still events (Q4=A); field-level restrictions enforced by validators.

        await writer.AppendAsync(eventId, EventTypes.EventDetailsChanged,
            new EventDetailsChangedPayload(eventId, name, venue, date, regStart, regEnd, entryFee), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> SetRegistrationOpenAsync(long accountId, long eventId, bool open, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(accountId, eventId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("Event.Forbidden", "Not an organizer on this event.");
        await writer.AppendAsync(eventId, open ? EventTypes.RegistrationOpened : EventTypes.RegistrationClosed,
            new RegistrationWindowPayload(eventId), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> SetPaymentOptionsAsync(long accountId, long eventId, bool cardEnabled, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(accountId, eventId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("Event.Forbidden", "Not an organizer on this event.");
        await writer.AppendAsync(eventId, EventTypes.PaymentOptionsChanged, new PaymentOptionsChangedPayload(eventId, cardEnabled), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> SetWeighInPolicyAsync(long accountId, long eventId, string mode, double? tolerance, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(accountId, eventId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("Event.Forbidden", "Not an organizer on this event.");
        var row = await db.EventRows.FindAsync([eventId], ct);
        if (row is null) return Error.NotFound("Event.NotFound", "Event not found.");
        if (row.CheckInStarted) return Error.Conflict("Event.PolicyLocked", "Weigh-in policy is locked once check-in has begun.");
        if (mode == nameof(WeighInPolicyMode.Tolerance) && tolerance is null)
            return Error.Validation("Event.WeighIn", "Tolerance policy requires a percentage.");
        await writer.AppendAsync(eventId, EventTypes.WeighInPolicyChanged, new WeighInPolicyChangedPayload(eventId, mode, tolerance), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<long>> ConfigureDivisionAsync(long accountId, ConfigureDivisionInput input, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(accountId, input.EventId, OrganizerAction.ConfigureDivisions, ct))
            return Error.Forbidden("Division.Forbidden", "Not an organizer on this event.");
        if (input.MinRank > input.MaxRank || input.MinAge > input.MaxAge)
            return Error.Validation("Division.Range", "Range bounds are inverted.");
        if (input.WeightLower is { } lo && lo > input.WeightUpper)
            return Error.Validation("Division.Weight", "Weight lower bound exceeds upper bound.");

        // BR-DIV-1: reject overlaps within the same gender/rank/age slice
        var siblings = await db.DivisionRows.AsNoTracking().Where(d => d.EventId == input.EventId).ToListAsync(ct);
        if (siblings.Any(d => Overlaps(d, input)))
            return Error.Conflict("Division.Overlap", "Division overlaps an existing division in the same slice.");

        var divisionId = ids.NextId();
        await writer.AppendAsync(input.EventId, EventTypes.DivisionConfigured, new DivisionConfiguredPayload(
            divisionId, input.EventId, input.WeightLower, input.WeightUpper, input.MinRank, input.MaxRank,
            input.MinAge, input.MaxAge, input.Gender, input.Format), ct);
        await db.SaveChangesAsync(ct);
        return divisionId;
    }

    private static bool Overlaps(DivisionRow a, ConfigureDivisionInput b)
    {
        if (!string.Equals(a.Gender, b.Gender, StringComparison.OrdinalIgnoreCase)) return false;
        var rankOverlap = a.MinRank <= b.MaxRank && b.MinRank <= a.MaxRank;
        var ageOverlap = a.MinAge <= b.MaxAge && b.MinAge <= a.MaxAge;
        var aLo = a.WeightLower ?? double.MinValue; var bLo = b.WeightLower ?? double.MinValue;
        var weightOverlap = aLo <= b.WeightUpper && bLo <= a.WeightUpper;
        return rankOverlap && ageOverlap && weightOverlap;
    }
}
