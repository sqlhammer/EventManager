using EventManager.Sync;

namespace EventManager.Judge.Core;

/// <summary>Judge-authored scoring event vocabulary. The hub (U4b) computes the authoritative outcome.</summary>
public static class JudgeEventTypes
{
    public const string PointSparringScored = "PointSparringScored";
    public const string FormsScored = "FormsScored";
}

public sealed record PointSparringScorePayload(long DivisionId, long MatchId,
    long CompetitorA, int PointsA, int PenaltiesA, long CompetitorB, int PointsB, int PenaltiesB);

public sealed record FormsCompetitorScore(long CompetitorId, IReadOnlyList<double> JudgeScores);
public sealed record FormsScorePayload(long DivisionId, long MatchId, IReadOnlyList<FormsCompetitorScore> Competitors);

/// <summary>
/// Captures point-sparring (US-402) and forms (US-403) scores. Every capture is **persisted durably
/// before the method returns** (durable-before-ack); the queued event replays to the hub, which owns
/// mat-authority validation and bracket advancement.
/// </summary>
public sealed class ScoreCaptureService(SpokeEventLog log)
{
    public Task<TournamentEvent> CapturePointSparringAsync(long eventScopeId, PointSparringScorePayload score, CancellationToken ct = default) =>
        log.AppendDurableAsync(eventScopeId, JudgeEventTypes.PointSparringScored, score, ct);

    public Task<TournamentEvent> CaptureFormsAsync(long eventScopeId, FormsScorePayload score, CancellationToken ct = default) =>
        log.AppendDurableAsync(eventScopeId, JudgeEventTypes.FormsScored, score, ct);
}
