using EventManager.Api.Events;
using EventManager.Api.Persistence;
using EventManager.Sync;

namespace EventManager.Api.Projections;

/// <summary>
/// Synchronous inline projection host (PP-2, Q2=A). Folds each appended/ingested <see cref="EventRecord"/>
/// into the read-model tables using the same DbContext, so writes are read-your-writes consistent.
/// Groups the five U3 projections (Event, Division, Roster, Organizer, Results). Unknown event types
/// are ignored — forward-compatible with the hub's fuller vocabulary (BR-ING-3).
/// </summary>
public sealed class CloudProjectionHost(AppDbContext db, IEventSerializer ser)
{
    public void Dispatch(EventRecord r)
    {
        switch (r.EventType)
        {
            // --- EventProjection ---
            case EventTypes.EventCreated: OnEventCreated(P<EventCreatedPayload>(r)); break;
            case EventTypes.EventDetailsChanged: OnEventDetailsChanged(P<EventDetailsChangedPayload>(r)); break;
            case EventTypes.RegistrationOpened: SetStatus(P<RegistrationWindowPayload>(r).EventId, RegistrationStatusRow.Open); break;
            case EventTypes.RegistrationClosed: SetStatus(P<RegistrationWindowPayload>(r).EventId, RegistrationStatusRow.Closed); break;
            case EventTypes.PaymentOptionsChanged: OnPaymentOptions(P<PaymentOptionsChangedPayload>(r)); break;
            case EventTypes.WeighInPolicyChanged: OnWeighInPolicy(P<WeighInPolicyChangedPayload>(r)); break;

            // --- DivisionProjection ---
            case EventTypes.DivisionConfigured:
            case EventTypes.DivisionUpdated: OnDivision(P<DivisionConfiguredPayload>(r)); break;

            // --- RosterProjection (profiles + registrations + payment) ---
            case EventTypes.AthleteProfileCreated:
            case EventTypes.AthleteProfileUpdated: OnProfile(P<AthleteProfilePayload>(r)); break;
            case EventTypes.RegistrationSubmitted: OnRegistrationSubmitted(P<RegistrationSubmittedPayload>(r)); break;
            case EventTypes.RegistrationEdited: OnRegistrationEdited(P<RegistrationEditedPayload>(r)); break;
            case EventTypes.RegistrationWithdrawn: OnRegistrationWithdrawn(P<RegistrationWithdrawnPayload>(r)); break;
            case EventTypes.PaymentStatusChanged: OnPaymentStatus(P<PaymentStatusChangedPayload>(r)); break;

            // --- OrganizerProjection ---
            case EventTypes.OrganizerAssigned: OnOrganizerAssigned(P<OrganizerAssignedPayload>(r)); break;
            case EventTypes.OrganizerRoleChanged: OnOrganizerRoleChanged(P<OrganizerRoleChangedPayload>(r)); break;
            case EventTypes.OrganizerRemoved: OnOrganizerRemoved(P<OrganizerRemovedPayload>(r)); break;

            // --- ResultsProjection (ingested only, Q6=A) ---
            case EventTypes.MatchCompleted: OnMatchCompleted(P<MatchCompletedPayload>(r)); break;
            case EventTypes.DivisionFinalized: OnDivisionFinalized(P<DivisionFinalizedPayload>(r)); break;

            default: break; // unknown ⇒ ignore (BR-ING-3)
        }
    }

    private T P<T>(EventRecord r) => ser.Deserialize<T>(r.Payload);

    // ---- EventProjection ----
    private void OnEventCreated(EventCreatedPayload p) => db.EventRows.Add(new EventRow
    {
        EventId = p.EventId, Name = p.Name, Venue = p.Venue, Date = p.Date,
        RegistrationStart = p.RegistrationStart, RegistrationEnd = p.RegistrationEnd, EntryFee = p.EntryFee,
        RegistrationStatus = RegistrationStatusRow.Draft, CardEnabled = false, CreatedByAccountId = p.CreatedByAccountId,
        WeighInPolicyMode = p.WeighInPolicyMode, WeighInTolerancePercent = p.WeighInTolerancePercent,
    });

    private void OnEventDetailsChanged(EventDetailsChangedPayload p)
    {
        var row = db.EventRows.Find(p.EventId); if (row is null) return;
        row.Name = p.Name; row.Venue = p.Venue; row.Date = p.Date;
        row.RegistrationStart = p.RegistrationStart; row.RegistrationEnd = p.RegistrationEnd; row.EntryFee = p.EntryFee;
    }

    private void SetStatus(long eventId, RegistrationStatusRow status)
    {
        var row = db.EventRows.Find(eventId); if (row is not null) row.RegistrationStatus = status;
    }

    private void OnPaymentOptions(PaymentOptionsChangedPayload p)
    {
        var row = db.EventRows.Find(p.EventId); if (row is not null) row.CardEnabled = p.CardEnabled;
    }

    private void OnWeighInPolicy(WeighInPolicyChangedPayload p)
    {
        var row = db.EventRows.Find(p.EventId); if (row is null) return;
        row.WeighInPolicyMode = p.Mode; row.WeighInTolerancePercent = p.TolerancePercent;
    }

    // ---- DivisionProjection ----
    private void OnDivision(DivisionConfiguredPayload p)
    {
        var row = db.DivisionRows.Find(p.DivisionId);
        if (row is null)
        {
            db.DivisionRows.Add(new DivisionRow
            {
                DivisionId = p.DivisionId, EventId = p.EventId, WeightLower = p.WeightLower, WeightUpper = p.WeightUpper,
                MinRank = p.MinRank, MaxRank = p.MaxRank, MinAge = p.MinAge, MaxAge = p.MaxAge, Gender = p.Gender, Format = p.Format,
            });
        }
        else
        {
            row.WeightLower = p.WeightLower; row.WeightUpper = p.WeightUpper; row.MinRank = p.MinRank; row.MaxRank = p.MaxRank;
            row.MinAge = p.MinAge; row.MaxAge = p.MaxAge; row.Gender = p.Gender; row.Format = p.Format;
        }
    }

    // ---- RosterProjection ----
    private void OnProfile(AthleteProfilePayload p)
    {
        var row = db.AthleteProfileRows.Find(p.AthleteId);
        if (row is null)
            db.AthleteProfileRows.Add(new AthleteProfileRow
            {
                AthleteId = p.AthleteId, OwnerAccountId = p.OwnerAccountId, Name = p.Name, DateOfBirth = p.DateOfBirth,
                Rank = p.Rank, Weight = p.Weight, Academy = p.Academy, Gender = p.Gender,
            });
        else
        {
            row.Name = p.Name; row.DateOfBirth = p.DateOfBirth; row.Rank = p.Rank; row.Weight = p.Weight;
            row.Academy = p.Academy; row.Gender = p.Gender;
        }
    }

    private void OnRegistrationSubmitted(RegistrationSubmittedPayload p) => db.RegistrationRows.Add(new RegistrationRow
    {
        RegistrationId = p.RegistrationId, EventId = p.EventId, AthleteId = p.AthleteId, ManagedByAccountId = p.ManagedByAccountId,
        AthleteName = p.AthleteName, Academy = p.Academy, DivisionIdsCsv = string.Join(',', p.DivisionIds),
        PaymentStatus = p.PaymentStatus, HasAssignmentMismatch = p.HasAssignmentMismatch, MismatchReasons = p.MismatchReasons,
    });

    private void OnRegistrationEdited(RegistrationEditedPayload p)
    {
        var row = db.RegistrationRows.Find(p.RegistrationId); if (row is null) return;
        row.AthleteName = p.AthleteName; row.Academy = p.Academy; row.DivisionIdsCsv = string.Join(',', p.DivisionIds);
        row.PaymentStatus = p.PaymentStatus; row.HasAssignmentMismatch = p.HasAssignmentMismatch; row.MismatchReasons = p.MismatchReasons;
    }

    private void OnRegistrationWithdrawn(RegistrationWithdrawnPayload p)
    {
        var row = db.RegistrationRows.Find(p.RegistrationId); if (row is not null) row.Withdrawn = true;
    }

    private void OnPaymentStatus(PaymentStatusChangedPayload p)
    {
        var row = db.RegistrationRows.Find(p.RegistrationId); if (row is not null) row.PaymentStatus = p.PaymentStatus;
    }

    // ---- OrganizerProjection ----
    private void OnOrganizerAssigned(OrganizerAssignedPayload p) => db.OrganizerRows.Add(new OrganizerRow
    {
        Id = p.Id, EventId = p.EventId, AccountId = p.AccountId, Role = p.Role,
    });

    private void OnOrganizerRoleChanged(OrganizerRoleChangedPayload p)
    {
        var row = db.OrganizerRows.Find(p.Id); if (row is not null) row.Role = p.Role;
    }

    private void OnOrganizerRemoved(OrganizerRemovedPayload p)
    {
        var row = db.OrganizerRows.Find(p.Id); if (row is not null) db.OrganizerRows.Remove(row);
    }

    // ---- ResultsProjection (Q6=A) ----
    private void OnMatchCompleted(MatchCompletedPayload p)
    {
        var row = FindResult(p.AthleteId, p.EventId, p.DivisionId);
        if (p.Won) row.Wins++; else row.Losses++;
        row.Status = "In Progress";
    }

    private void OnDivisionFinalized(DivisionFinalizedPayload p)
    {
        foreach (var placement in p.Placements)
        {
            var row = FindResult(placement.AthleteId, p.EventId, p.DivisionId);
            row.Placement = placement.Placement;
            row.Status = "Final";
        }
    }

    private ResultRow FindResult(long athleteId, long eventId, long divisionId)
    {
        var row = db.ResultRows.Local.FirstOrDefault(x => x.AthleteId == athleteId && x.EventId == eventId && x.DivisionId == divisionId)
            ?? db.ResultRows.FirstOrDefault(x => x.AthleteId == athleteId && x.EventId == eventId && x.DivisionId == divisionId);
        if (row is null)
        {
            row = new ResultRow { AthleteId = athleteId, EventId = eventId, DivisionId = divisionId, Status = "In Progress" };
            db.ResultRows.Add(row);
        }
        return row;
    }
}
