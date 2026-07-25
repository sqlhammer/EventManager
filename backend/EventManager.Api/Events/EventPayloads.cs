namespace EventManager.Api.Events;

/// <summary>Canonical event-type names for the U3-authored domain vocabulary + the ingested
/// result subset the ResultsProjection understands (Q6=A). Stored in <c>TournamentEvent.EventType</c>.</summary>
public static class EventTypes
{
    public const string EventCreated = "EventCreated";
    public const string EventDetailsChanged = "EventDetailsChanged";
    public const string RegistrationOpened = "RegistrationOpened";
    public const string RegistrationClosed = "RegistrationClosed";
    public const string PaymentOptionsChanged = "PaymentOptionsChanged";
    public const string WeighInPolicyChanged = "WeighInPolicyChanged";

    public const string DivisionConfigured = "DivisionConfigured";
    public const string DivisionUpdated = "DivisionUpdated";

    public const string AthleteProfileCreated = "AthleteProfileCreated";
    public const string AthleteProfileUpdated = "AthleteProfileUpdated";

    public const string RegistrationSubmitted = "RegistrationSubmitted";
    public const string RegistrationEdited = "RegistrationEdited";
    public const string RegistrationWithdrawn = "RegistrationWithdrawn";
    public const string PaymentStatusChanged = "PaymentStatusChanged";

    public const string OrganizerAssigned = "OrganizerAssigned";
    public const string OrganizerRoleChanged = "OrganizerRoleChanged";
    public const string OrganizerRemoved = "OrganizerRemoved";

    // Ingested (authored by the hub/U4b; folded by ResultsProjection only)
    public const string MatchCompleted = "MatchCompleted";
    public const string DivisionFinalized = "DivisionFinalized";
}

// --- Event payloads (serialized into TournamentEvent.Payload via JsonEventSerializer) ---

public sealed record EventCreatedPayload(long EventId, string Name, string Venue, DateOnly Date,
    DateOnly RegistrationStart, DateOnly RegistrationEnd, decimal EntryFee, long CreatedByAccountId,
    string WeighInPolicyMode, double? WeighInTolerancePercent);

public sealed record EventDetailsChangedPayload(long EventId, string Name, string Venue, DateOnly Date,
    DateOnly RegistrationStart, DateOnly RegistrationEnd, decimal EntryFee);

public sealed record RegistrationWindowPayload(long EventId);

public sealed record PaymentOptionsChangedPayload(long EventId, bool CardEnabled);

public sealed record WeighInPolicyChangedPayload(long EventId, string Mode, double? TolerancePercent);

public sealed record DivisionConfiguredPayload(long DivisionId, long EventId, double? WeightLower,
    double WeightUpper, int MinRank, int MaxRank, int MinAge, int MaxAge, string Gender, string Format);

public sealed record AthleteProfilePayload(long AthleteId, long OwnerAccountId, string Name,
    DateOnly DateOfBirth, int Rank, double Weight, string Academy, string Gender);

public sealed record RegistrationSubmittedPayload(long RegistrationId, long EventId, long AthleteId,
    long ManagedByAccountId, string AthleteName, string Academy, IReadOnlyList<long> DivisionIds,
    string PaymentStatus, bool HasAssignmentMismatch, string? MismatchReasons);

public sealed record RegistrationEditedPayload(long RegistrationId, string AthleteName, string Academy,
    IReadOnlyList<long> DivisionIds, string PaymentStatus, bool HasAssignmentMismatch, string? MismatchReasons);

public sealed record RegistrationWithdrawnPayload(long RegistrationId);

public sealed record PaymentStatusChangedPayload(long RegistrationId, string PaymentStatus);

public sealed record OrganizerAssignedPayload(long Id, long EventId, long AccountId, string Role);

public sealed record OrganizerRoleChangedPayload(long Id, long EventId, long AccountId, string Role);

public sealed record OrganizerRemovedPayload(long Id, long EventId, long AccountId);

// Ingested result payloads (subset)
public sealed record MatchCompletedPayload(long AthleteId, long EventId, long DivisionId, bool Won);

public sealed record DivisionFinalizedPayload(long EventId, long DivisionId,
    IReadOnlyList<AthletePlacement> Placements);

public sealed record AthletePlacement(long AthleteId, int Placement);
