namespace EventManager.Hub.Competition;

/// <summary>Hub-authored competition event vocabulary (U4b). Appended to the hub log for audit +
/// replication; the competition read models are updated transactionally by the services.</summary>
public static class CompetitionEventTypes
{
    public const string BracketGenerated = "BracketGenerated";
    public const string BracketAdvanced = "BracketAdvanced";
    public const string DivisionStarted = "DivisionStarted";
    public const string WeighInResolved = "WeighInResolved";
    public const string DivisionMoved = "DivisionMoved";
    public const string DisputeFlagged = "DisputeFlagged";
    public const string DisputeResolved = "DisputeResolved";
    public const string DivisionFinalized = "DivisionFinalized";
}

public sealed record BracketGeneratedPayload(long DivisionId, long EventId, string Format, IReadOnlyList<MatchDto> Matches);
public sealed record BracketAdvancedPayload(long DivisionId, long MatchId, long WinnerId, string Method);
public sealed record DivisionStartedPayload(long DivisionId);
public sealed record WeighInResolvedPayload(long DivisionId, long AthleteId, string Result, long? TargetDivisionId);
public sealed record DivisionMovedPayload(long AthleteId, long FromDivisionId, long ToDivisionId);
public sealed record DisputeFlaggedPayload(long DisputeId, long DivisionId, long MatchId, string Reason);
public sealed record DisputeResolvedPayload(long DisputeId, string Resolution);
public sealed record DivisionFinalizedPayload(long DivisionId, IReadOnlyList<PlacementDto> Placements);

public sealed record PlacementDto(long RegistrationId, int Placement);
