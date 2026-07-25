using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using ErrorOr;

namespace EventManager.Hub.Competition;

/// <summary>
/// Missed-weight policy resolution (US-308) and division move (US-309). Delegates the decision to the
/// U1 WeighInPolicyEvaluator, records it as an event, and — when the policy moves the athlete and the
/// target division has not started — signals a move so brackets regenerate (via BracketService).
/// </summary>
public sealed class WeighInResolutionService(HubDbContext db, HubEventWriter writer, IWeighInPolicyEvaluator evaluator)
{
    public async Task<ErrorOr<WeighInOutcome>> ResolveAsync(long eventId, long athleteId, double weight,
        Division division, WeighInPolicy policy, IReadOnlyList<Division> autoMoveCandidates, CancellationToken ct = default)
    {
        var evaluated = evaluator.Evaluate(weight, division, policy, autoMoveCandidates);
        if (evaluated.IsError) return evaluated.Errors;

        var outcome = evaluated.Value;
        long? targetDivisionId = null;
        if (outcome.TargetDivisionId is { } target) targetDivisionId = target.Value;

        await writer.AppendAsync(eventId, CompetitionEventTypes.WeighInResolved,
            new WeighInResolvedPayload(division.DivisionId.Value, athleteId, outcome.Result.ToString(), targetDivisionId), ct);

        if (outcome.Result == WeighInResult.Moved && targetDivisionId is { } to)
        {
            await writer.AppendAsync(eventId, CompetitionEventTypes.DivisionMoved,
                new DivisionMovedPayload(athleteId, division.DivisionId.Value, to), ct);
            // Bracket regeneration for both affected divisions is orchestrated by the caller via
            // BracketService.GenerateAsync once the updated cleared field is known (US-309). Moves into a
            // started division are refused by BracketService's start guard.
        }

        await db.SaveChangesAsync(ct);
        return outcome;
    }
}
