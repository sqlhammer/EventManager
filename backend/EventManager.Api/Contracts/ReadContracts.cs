namespace EventManager.Api.Contracts;

// ---------------------------------------------------------------------------
// U9 read response shapes. Shape is a function of the caller's resolved tier
// (BR-READ-7) — never of an endpoint or a client-supplied parameter.
// ---------------------------------------------------------------------------

/// <summary>Public-tier event view (US-701). The registration window is present at this tier by
/// design (Q4=C): discovery keys off status alone, and the window lets a client render an event as
/// expired without the API concealing it.</summary>
public sealed record EventSummaryResponse(
    long EventId,
    string Name,
    string Venue,
    DateOnly Date,
    DateOnly RegistrationStart,
    DateOnly RegistrationEnd,
    decimal EntryFee,
    string RegistrationStatus);

/// <summary>Registrant/Organizer-tier event view (US-702, US-703).</summary>
public sealed record EventDetailResponse(
    long EventId,
    string Name,
    string Venue,
    DateOnly Date,
    DateOnly RegistrationStart,
    DateOnly RegistrationEnd,
    decimal EntryFee,
    string RegistrationStatus,
    bool CardEnabled,
    bool CheckInStarted,
    WeighInPolicyResponse WeighInPolicy,
    long CreatedByAccountId);

/// <summary>Collection item — summary plus the caller's effective tier, so a client knows which
/// events it may open for detail without probing (US-704).</summary>
public sealed record EventListItemResponse(
    long EventId,
    string Name,
    string Venue,
    DateOnly Date,
    DateOnly RegistrationStart,
    DateOnly RegistrationEnd,
    decimal EntryFee,
    string RegistrationStatus,
    string AccessTier,
    string? OrganizerRole);

public sealed record DivisionResponse(
    long DivisionId,
    long EventId,
    double? WeightLower,
    double WeightUpper,
    int MinRank,
    int MaxRank,
    int MinAge,
    int MaxAge,
    string Gender,
    string Format,
    string Status);

/// <summary><c>TolerancePercent</c> is populated only when <c>Mode</c> is Tolerance (BR-READ-12).</summary>
public sealed record WeighInPolicyResponse(string Mode, double? TolerancePercent);

/// <summary>Organizer-tier list item. Carries NO date of birth, weight, rank, or gender (BR-READ-8).</summary>
public sealed record RegistrantListItemResponse(
    long RegistrationId,
    long AthleteId,
    string AthleteName,
    string Academy,
    IReadOnlyList<long> DivisionIds,
    string PaymentStatus,
    bool HasAssignmentMismatch,
    bool Withdrawn);

/// <summary>Detail shape — adds the profile fields organizers need for weigh-in checks (BR-READ-9).
/// This is the only shape reading data outside the event scope, which is why it carries no ETag
/// (BR-READ-26, U9-CON-2).</summary>
public sealed record RegistrantDetailResponse(
    long RegistrationId,
    long AthleteId,
    string AthleteName,
    string Academy,
    IReadOnlyList<long> DivisionIds,
    string PaymentStatus,
    bool HasAssignmentMismatch,
    string? MismatchReasons,
    bool Withdrawn,
    DateOnly? DateOfBirth,
    double? Weight,
    int? Rank,
    string? Gender);

/// <summary>Organizer roster entry. Never carries credential, MFA, or session material (BR-READ-11).</summary>
public sealed record OrganizerAccountResponse(long AccountId, string Email, string Role);
