using EventManager.Api.Auth;
using EventManager.Api.Events;
using EventManager.Api.Persistence;
using EventManager.Domain;
using EventManager.Payments;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

public sealed record ProfileInput(string Name, DateOnly DateOfBirth, int Rank, double Weight, string Academy, string Gender);
public sealed record RegisterInput(long EventId, long AthleteId, IReadOnlyList<long> SelectedDivisionIds, bool PayByCard);
public sealed record BatchEntry(long AthleteId, IReadOnlyList<long> SelectedDivisionIds);
public sealed record BatchRegisterInput(long EventId, IReadOnlyList<BatchEntry> Entries, bool PayByCard, string IdempotencyKey);

public sealed record RegistrationResult(long RegistrationId, decimal FeeTotal, string PaymentStatus, bool HasMismatch, string? MismatchReasons);
public sealed record BatchProblem(long AthleteId, string Reason);
public sealed record BatchResult(IReadOnlyList<long> RegistrationIds, decimal CombinedFeeTotal, string PaymentStatus);

/// <summary>
/// S-1 registration (US-201–207, 209–211). Validate → assign divisions (eligibility, Q3=A) → durable
/// event append → project. Bulk is atomic with itemized conflicts (Q2=A). Payment delegates to the
/// U8 stub; decline/timeout leaves the registration Owed with a retry path (BR-PAY-3).
/// </summary>
public sealed class RegistrationService(
    AppDbContext db, EventWriter writer, EventManager.Sync.IIdGenerator ids,
    IdempotencyStore idempotency, IPaymentProvider payments, EventAuthorizer authz)
{
    public async Task<ErrorOr<long>> UpsertProfileAsync(long ownerAccountId, long? athleteId, ProfileInput p, CancellationToken ct = default)
    {
        if (p.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow)) return Error.Validation("Profile.Dob", "Date of birth is in the future.");
        if (p.Weight is <= 0 or > 500) return Error.Validation("Profile.Weight", "Weight out of plausible bounds.");

        var id = athleteId ?? ids.NextId();
        var existing = athleteId is not null;
        if (existing)
        {
            var owned = await db.AthleteProfileRows.AnyAsync(a => a.AthleteId == id && a.OwnerAccountId == ownerAccountId, ct);
            if (!owned) return Error.Forbidden("Profile.Forbidden", "Profile not owned by this account.");
        }
        var profileEventType = EventTypes.AthleteProfileCreated;
        if (existing) profileEventType = EventTypes.AthleteProfileUpdated;
        await writer.AppendAsync(id, profileEventType,
            new AthleteProfilePayload(id, ownerAccountId, p.Name, p.DateOfBirth, p.Rank, p.Weight, p.Academy, p.Gender), ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task<ErrorOr<RegistrationResult>> RegisterAsync(long callerAccountId, RegisterInput input, CancellationToken ct = default)
    {
        var evt = await db.EventRows.FindAsync([input.EventId], ct);
        if (evt is null) return Error.NotFound("Reg.Event", "Event not found.");
        if (evt.RegistrationStatus != RegistrationStatusRow.Open) return Error.Conflict("Reg.Window", "Registration window is not open.");

        var profile = await db.AthleteProfileRows.FindAsync([input.AthleteId], ct);
        if (profile is null) return Error.NotFound("Reg.Profile", "Athlete profile not found.");
        // BR-REG-8: caller must own/manage the athlete (self or parent).
        if (profile.OwnerAccountId != callerAccountId) return Error.Forbidden("Reg.Forbidden", "Not permitted to register this athlete.");

        var conflict = await ValidateSelectionAsync(evt, profile, input.SelectedDivisionIds, checkDuplicate: true, ct);
        if (conflict is not null) return Error.Validation("Reg.Selection", conflict);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await AppendRegistrationAsync(evt, profile, input.SelectedDivisionIds, input.PayByCard, callerAccountId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<ErrorOr<BatchResult>> RegisterBatchAsync(long coachAccountId, BatchRegisterInput input, CancellationToken ct = default)
    {
        // BR-REG-7: idempotent resubmit — return the recorded first result.
        var prior = await idempotency.TryGetAsync<BatchResult>(input.IdempotencyKey, ct);
        if (prior is not null) return prior;

        var evt = await db.EventRows.FindAsync([input.EventId], ct);
        if (evt is null) return Error.NotFound("Reg.Event", "Event not found.");
        if (evt.RegistrationStatus != RegistrationStatusRow.Open) return Error.Conflict("Reg.Window", "Registration window is not open.");

        // Phase 1: validate the whole batch (Q2=A). Any problem ⇒ commit nothing (BR-REG-6).
        var problems = new List<BatchProblem>();
        var profiles = new Dictionary<long, AthleteProfileRow>();
        foreach (var entry in input.Entries)
        {
            var profile = await db.AthleteProfileRows.FindAsync([entry.AthleteId], ct);
            if (profile is null) { problems.Add(new BatchProblem(entry.AthleteId, "Profile not found.")); continue; }
            if (profile.OwnerAccountId != coachAccountId) { problems.Add(new BatchProblem(entry.AthleteId, "Athlete not on your roster.")); continue; }
            var conflict = await ValidateSelectionAsync(evt, profile, entry.SelectedDivisionIds, checkDuplicate: true, ct);
            if (conflict is not null) { problems.Add(new BatchProblem(entry.AthleteId, conflict)); continue; }
            profiles[entry.AthleteId] = profile;
        }
        if (problems.Count > 0)
            return Error.Validation("Reg.Batch", "Itemized conflicts: " +
                string.Join("; ", problems.Select(p => $"athlete {p.AthleteId}: {p.Reason}")));

        // Phase 2: commit all atomically under one transaction + one fee summary.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var regIds = new List<long>();
        decimal combined = 0;
        foreach (var entry in input.Entries)
        {
            var profile = profiles[entry.AthleteId];
            // Card charging for the batch is done once below; per-registration status set after.
            var r = await AppendRegistrationAsync(evt, profile, entry.SelectedDivisionIds, payByCard: false, coachAccountId, ct);
            regIds.Add(r.RegistrationId);
            combined += r.FeeTotal;
        }

        var status = nameof(PaymentStatus.Owed);
        if (input.PayByCard && evt.CardEnabled)
        {
            var pay = await OutboundRetry.ExecuteAsync(c => payments.ChargeAsync(
                new PaymentRequest(regIds[0], combined, "USD", input.IdempotencyKey), c), ct: ct);
            if (pay.IsSuccess)
            {
                status = nameof(PaymentStatus.Paid);
                foreach (var rid in regIds)
                    await writer.AppendAsync(input.EventId, EventTypes.PaymentStatusChanged, new PaymentStatusChangedPayload(rid, status), ct);
            }
            // decline/timeout ⇒ leave Owed (BR-PAY-3)
        }

        var result = new BatchResult(regIds, combined, status);
        idempotency.Record(input.IdempotencyKey, result);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<ErrorOr<RegistrationResult>> EditAsync(long callerAccountId, long registrationId, IReadOnlyList<long> selectedDivisionIds, CancellationToken ct = default)
    {
        var reg = await db.RegistrationRows.FindAsync([registrationId], ct);
        if (reg is null || reg.Withdrawn) return Error.NotFound("Reg.NotFound", "Registration not found.");
        if (reg.ManagedByAccountId != callerAccountId) return Error.Forbidden("Reg.Forbidden", "Not permitted to edit.");
        var evt = await db.EventRows.FindAsync([reg.EventId], ct);
        if (evt is null) return Error.NotFound("Reg.Event", "Event not found.");
        if (evt.RegistrationStatus != RegistrationStatusRow.Open) return Error.Conflict("Reg.Window", "Window closed — organizer must edit.");
        var profile = await db.AthleteProfileRows.FindAsync([reg.AthleteId], ct);
        if (profile is null) return Error.NotFound("Reg.Profile", "Profile not found.");

        var (assigned, mismatch, reasons) = ResolveAssignment(evt, profile, selectedDivisionIds,
            await DivisionsFor(evt.EventId, ct));
        var fee = evt.EntryFee * assigned.Count;
        await writer.AppendAsync(reg.EventId, EventTypes.RegistrationEdited, new RegistrationEditedPayload(
            registrationId, profile.Name, profile.Academy, assigned, reg.PaymentStatus, mismatch, reasons), ct);
        await db.SaveChangesAsync(ct);
        return new RegistrationResult(registrationId, fee, reg.PaymentStatus, mismatch, reasons);
    }

    public async Task<ErrorOr<Success>> WithdrawAsync(long callerAccountId, long registrationId, CancellationToken ct = default)
    {
        var reg = await db.RegistrationRows.FindAsync([registrationId], ct);
        if (reg is null || reg.Withdrawn) return Error.NotFound("Reg.NotFound", "Registration not found.");
        if (reg.ManagedByAccountId != callerAccountId) return Error.Forbidden("Reg.Forbidden", "Not permitted to withdraw.");
        await writer.AppendAsync(reg.EventId, EventTypes.RegistrationWithdrawn, new RegistrationWithdrawnPayload(registrationId), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    /// <summary>Organizer marks an owed balance Paid (cash) or Waived (US-209, BR-PAY-2).</summary>
    public async Task<ErrorOr<Success>> SetPaymentStatusAsync(long organizerAccountId, long registrationId, string status, CancellationToken ct = default)
    {
        if (status is not (nameof(PaymentStatus.Paid) or nameof(PaymentStatus.Waived) or nameof(PaymentStatus.Owed)))
            return Error.Validation("Reg.Payment", "Invalid payment status.");
        var reg = await db.RegistrationRows.FindAsync([registrationId], ct);
        if (reg is null) return Error.NotFound("Reg.NotFound", "Registration not found.");
        if (!await authz.IsPermittedAsync(organizerAccountId, reg.EventId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("Reg.Forbidden", "Not an organizer on this event.");
        await writer.AppendAsync(reg.EventId, EventTypes.PaymentStatusChanged, new PaymentStatusChangedPayload(registrationId, status), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    // ---- helpers ----

    private async Task<RegistrationResult> AppendRegistrationAsync(EventRow evt, AthleteProfileRow profile,
        IReadOnlyList<long> selected, bool payByCard, long callerAccountId, CancellationToken ct)
    {
        var (assigned, mismatch, reasons) = ResolveAssignment(evt, profile, selected, await DivisionsFor(evt.EventId, ct));
        var fee = evt.EntryFee * assigned.Count;
        var regId = ids.NextId();

        var status = nameof(PaymentStatus.Owed);
        if (payByCard && evt.CardEnabled)
        {
            var pay = await OutboundRetry.ExecuteAsync(c => payments.ChargeAsync(
                new PaymentRequest(regId, fee, "USD", $"reg:{regId}"), c), ct: ct);
            if (pay.IsSuccess) status = nameof(PaymentStatus.Paid);   // else Owed + retry (BR-PAY-3)
        }

        await writer.AppendAsync(evt.EventId, EventTypes.RegistrationSubmitted, new RegistrationSubmittedPayload(
            regId, evt.EventId, profile.AthleteId, callerAccountId, profile.Name, profile.Academy, assigned, status, mismatch, reasons), ct);
        return new RegistrationResult(regId, fee, status, mismatch, reasons);
    }

    private (IReadOnlyList<long> Assigned, bool Mismatch, string? Reasons) ResolveAssignment(
        EventRow evt, AthleteProfileRow profile, IReadOnlyList<long> selected, IReadOnlyList<DivisionRow> divisions)
    {
        var prof = new EligibilityProfile(profile.Weight, profile.Rank,
            DivisionEligibility.AgeAt(profile.DateOfBirth, evt.Date), profile.Gender);
        var eligible = DivisionEligibility.EligibleDivisionIds(prof, divisions).ToHashSet();
        var assigned = selected.Where(eligible.Contains).OrderBy(x => x).ToList();
        var rejected = selected.Where(s => !eligible.Contains(s)).ToList();
        var mismatch = rejected.Count > 0;
        string? reasons = null;
        if (mismatch) reasons = $"Not eligible for divisions: {string.Join(',', rejected)}";
        return (assigned, mismatch, reasons);
    }

    private async Task<string?> ValidateSelectionAsync(EventRow evt, AthleteProfileRow profile, IReadOnlyList<long> selected, bool checkDuplicate, CancellationToken ct)
    {
        if (selected.Count == 0) return "No divisions selected.";
        var divisions = await DivisionsFor(evt.EventId, ct);
        var prof = new EligibilityProfile(profile.Weight, profile.Rank,
            DivisionEligibility.AgeAt(profile.DateOfBirth, evt.Date), profile.Gender);
        var eligible = DivisionEligibility.EligibleDivisionIds(prof, divisions).ToHashSet();
        var ineligible = selected.Where(s => !eligible.Contains(s)).ToList();
        if (ineligible.Count > 0) return $"Ineligible for divisions {string.Join(',', ineligible)}.";

        if (checkDuplicate)
        {
            // BR-REG-5: no double-registration for the same division.
            var existing = await db.RegistrationRows
                .Where(r => r.EventId == evt.EventId && r.AthleteId == profile.AthleteId && !r.Withdrawn)
                .Select(r => r.DivisionIdsCsv).ToListAsync(ct);
            var already = existing.SelectMany(csv => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)).Select(long.Parse).ToHashSet();
            var dup = selected.Where(already.Contains).ToList();
            if (dup.Count > 0) return $"Already registered for divisions {string.Join(',', dup)}.";
        }
        return null;
    }

    private async Task<IReadOnlyList<DivisionRow>> DivisionsFor(long eventId, CancellationToken ct) =>
        await db.DivisionRows.AsNoTracking().Where(d => d.EventId == eventId).ToListAsync(ct);
}
