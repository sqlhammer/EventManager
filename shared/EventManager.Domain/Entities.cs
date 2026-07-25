namespace EventManager.Domain;

/// <summary>Registration window for an event.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End);

/// <summary>Root aggregate for a tournament (FR-2.1).</summary>
public record EventDefinition(
    Snowflake EventId,
    string Name,
    string Venue,
    DateOnly Date,
    DateRange RegistrationWindow,
    decimal EntryFee,
    WeighInPolicy WeighInPolicy,
    ScoringConfig ScoringConfig);

/// <summary>Organizer↔event RBAC assignment (FR-1.6).</summary>
public record OrganizerRoleAssignment(Snowflake Id, Snowflake EventId, Snowflake AccountId, OrganizerRole Role);

public record Division(
    Snowflake DivisionId,
    Snowflake EventId,
    DivisionCriteria Criteria,
    BracketFormat Format,
    DivisionStatus Status);

public record AthleteProfile(
    Snowflake AthleteId,
    string Name,
    DateOnly DateOfBirth,
    int Rank,
    double Weight,
    string Academy,
    string Gender);

public record Registration(
    Snowflake RegistrationId,
    Snowflake EventId,
    Snowflake AthleteId,
    IReadOnlyList<Snowflake> DivisionIds,
    AthleteProfile Snapshot,
    PaymentStatus PaymentStatus);

/// <summary>A single bracket match. Null competitor = Bye. Outcome null until played.</summary>
public record Match(
    Snowflake MatchId,
    int RoundIndex,
    int SlotIndex,
    Snowflake? CompetitorA,
    Snowflake? CompetitorB,
    MatchOutcome? Outcome = null);

public record Bracket(
    Snowflake BracketId,
    Snowflake DivisionId,
    BracketFormat Format,
    IReadOnlyList<Seed> Seeds,
    IReadOnlyList<Match> Matches,
    DivisionStatus Status);

public record WeighIn(
    Snowflake WeighInId,
    Snowflake AthleteId,
    Snowflake DivisionId,
    double RecordedWeight,
    WeighInOutcome Outcome,
    Recommendation? Recommendation = null);

public record CheckIn(Snowflake CheckInId, Snowflake AthleteId, Snowflake EventId, DateTimeOffset At);

public record DeviceCredential(
    Snowflake DeviceId,
    Snowflake EventId,
    string RoleDescriptor,
    int WorkerId,
    bool Revoked = false);

public record PaymentRecord(Snowflake PaymentId, Snowflake RegistrationId, PaymentMethod Method, PaymentStatus State);
