using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Hub.Services;
using ErrorOr;

namespace EventManager.Hub.Competition;

/// <summary>
/// Scoring intake with mat-authority enforcement (US-404/406). A device may only score matches in the
/// division/mat it is assigned to — a foreign-mat write is rejected. The outcome is computed by the U1
/// ScoringEngine and applied to the bracket (advancement). Read-only cross-mat views never write.
/// </summary>
public sealed class ScoringIntakeService(DeviceRegistry devices, IScoringEngine scoring, BracketService brackets)
{
    public async Task<ErrorOr<Success>> SubmitPointSparringAsync(long deviceId, long eventId, long divisionId, long matchId,
        PointSparringInput input, PointSparringConfig config, CancellationToken ct = default)
    {
        var matAuthority = await CheckMatAuthorityAsync(deviceId, divisionId, ct);
        if (matAuthority.IsError) return matAuthority.Errors;

        var outcome = scoring.ScorePointSparring(input, config);
        if (outcome.IsError) return outcome.Errors;
        if (outcome.Value.Winner is not { } winner)
            return Error.Validation("Scoring.NoWinner", "Scoring produced no winner.");

        return await brackets.AdvanceAsync(eventId, divisionId, matchId, winner.Value, outcome.Value.Method, ct);
    }

    private async Task<ErrorOr<Success>> CheckMatAuthorityAsync(long deviceId, long divisionId, CancellationToken ct)
    {
        if (!await devices.IsActiveAsync(deviceId, ct))
            return Error.Forbidden("Scoring.Revoked", "Device credential is revoked or unknown.");

        var assigned = await devices.AssignedDivisionAsync(deviceId, ct);
        if (assigned != divisionId)
            return Error.Forbidden("Scoring.MatAuthority", "Device is not the mat authority for this division."); // US-406
        return Result.Success;
    }
}
