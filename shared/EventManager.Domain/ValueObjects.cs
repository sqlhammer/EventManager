namespace EventManager.Domain;

/// <summary>Weight class bounds for a division. Upper bound drives weigh-in tolerance (Q6).</summary>
public readonly record struct WeightClass(double? LowerBound, double UpperBound);

public readonly record struct AgeRange(int MinAge, int MaxAge);

public readonly record struct RankRange(int MinRank, int MaxRank);

/// <summary>Criteria used to auto-assign registrations to a division (FR-3.1).</summary>
public record DivisionCriteria(
    WeightClass WeightClass,
    RankRange RankRange,
    AgeRange AgeRange,
    string Gender);

/// <summary>Missed-weight policy (D-11 / FR-5.3). Tolerance percent required when mode = Tolerance.</summary>
public record WeighInPolicy(WeighInPolicyMode Mode, double? TolerancePercent = null);

/// <summary>Configurable penalty policy for point-sparring (Q2=D).</summary>
public record PenaltyPolicy(PenaltyMode Mode, int Cap);

/// <summary>
/// Point-sparring configuration (Q1=A): higher total wins; optional target-score and
/// mercy-gap early finish; configurable penalties.
/// </summary>
public record PointSparringConfig(PenaltyPolicy PenaltyPolicy, int? TargetScore = null, int? MercyGap = null);

/// <summary>Forms/kata configuration (Q3=A): drop high+low when judges &gt;= threshold.</summary>
public record FormsConfig(int DropHighLowWhenJudgesAtLeast = 5);

public record ScoringConfig(PointSparringConfig PointSparring, FormsConfig Forms);

/// <summary>A seeded competitor position in a bracket.</summary>
public record Seed(Snowflake RegistrationId, int SeedNumber, string? Academy = null);

/// <summary>Result of a completed match.</summary>
public record MatchOutcome(Snowflake? Winner, MatchMethod Method, string? Detail = null);

/// <summary>Outcome of evaluating a weigh-in against policy (FR-5.3).</summary>
public record WeighInOutcome(WeighInResult Result, Snowflake? TargetDivisionId = null);

/// <summary>Non-binding staff recommendation attached to an out-of-range weigh-in (D-25).</summary>
public record Recommendation(WeighInPolicyMode Suggested, Snowflake ByDeviceId);

// --- Scoring inputs / results (pure engine I/O) ---

public record PointSparringInput(
    Snowflake CompetitorA, int PointsA, int PenaltiesA,
    Snowflake CompetitorB, int PointsB, int PenaltiesB);

public record CompetitorForms(Snowflake CompetitorId, IReadOnlyList<double> JudgeScores);

public record FormsInput(IReadOnlyList<CompetitorForms> Competitors);

public record FormsPlacement(Snowflake CompetitorId, double Aggregate, int Rank);

public record FormsResult(IReadOnlyList<FormsPlacement> Placements);

public record SeedingOptions(int RandomSeed = 0);
