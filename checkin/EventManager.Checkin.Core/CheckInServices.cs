using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Sync;

namespace EventManager.Checkin.Core;

/// <summary>Check-In-authored event vocabulary. Append-only; the hub folds these into its boards.</summary>
public static class CheckinEventTypes
{
    public const string AthleteCheckedIn = "AthleteCheckedIn";
    public const string WeighInRecorded = "WeighInRecorded";
}

public sealed record CheckInPayload(long AthleteId, long EventId);

public sealed record WeighInRecordedPayload(long AthleteId, long DivisionId, double Weight, string Result,
    string? RecommendedResolution);

/// <summary>Instant weigh-in feedback for staff at the scale (US-307).</summary>
public sealed record WeighInFeedback(bool InRange, WeighInResult Result, long? TargetDivisionId);

/// <summary>
/// Check-in capture (US-306). Marking present is durably recorded as an append-only event before ack
/// (NFR-1.1) and becomes visible on the hub in real time.
/// </summary>
public sealed class CheckInService(SpokeEventLog log)
{
    public Task<TournamentEvent> CheckInAsync(long eventScopeId, long athleteId, CancellationToken ct = default) =>
        log.AppendDurableAsync(eventScopeId, CheckinEventTypes.AthleteCheckedIn, new CheckInPayload(athleteId, eventScopeId), ct);
}

/// <summary>
/// Weigh-in with range validation (US-307). Uses the U1 <see cref="IWeighInPolicyEvaluator"/> for
/// instant in/out-of-range feedback at the scale, records the weight as immutable history, and lets
/// staff attach an optional **non-binding** recommended resolution (D-25) surfaced to the organizer.
/// </summary>
public sealed class WeighInService(SpokeEventLog log, IWeighInPolicyEvaluator evaluator)
{
    public async Task<WeighInFeedback> RecordAsync(long eventScopeId, long athleteId, double weight,
        Division division, WeighInPolicy policy, IReadOnlyList<Division> autoMoveCandidates,
        WeighInPolicyMode? recommendedResolution = null, CancellationToken ct = default)
    {
        var evaluated = evaluator.Evaluate(weight, division, policy, autoMoveCandidates);
        if (evaluated.IsError)
            throw new InvalidOperationException(evaluated.FirstError.Description);

        var outcome = evaluated.Value;
        var inRange = outcome.Result is WeighInResult.Pass or WeighInResult.TolerancePass;

        long? targetDivisionId = null;
        if (outcome.TargetDivisionId is { } target) targetDivisionId = target.Value;

        string? recommendation = null;
        if (recommendedResolution is { } rec) recommendation = rec.ToString();

        await log.AppendDurableAsync(eventScopeId, CheckinEventTypes.WeighInRecorded,
            new WeighInRecordedPayload(athleteId, division.DivisionId.Value, weight, outcome.Result.ToString(), recommendation), ct);

        return new WeighInFeedback(inRange, outcome.Result, targetDivisionId);
    }
}
