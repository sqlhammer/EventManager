using EventManager.Domain;
using EventManager.Hub.Competition;
using EventManager.Hub.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Controllers;

public sealed record AdvanceMatchRequest(long MatchId, long WinnerId, string Method);
public sealed record FlagDisputeRequest(long DivisionId, long MatchId, string Reason);
public sealed record ResolveDisputeRequest(string Resolution);
public sealed record AssignMatRequest(long DeviceId, long DivisionId);

/// <summary>Competition endpoints (US-311..314, 404, 405, 601): bracket lifecycle, disputes, mat assignment.</summary>
[Route("api/events/{eventId:long}/competition")]
public sealed class CompetitionController(
    BracketService brackets, DivisionFinalizationService finalization, DisputeService disputes,
    Services.DeviceRegistry devices, HubDbContext db) : HubControllerBase
{
    [HttpPost("divisions/{divisionId:long}/bracket:advance")]
    public async Task<IActionResult> Advance(long eventId, long divisionId, AdvanceMatchRequest req, CancellationToken ct)
    {
        var method = Enum.Parse<MatchMethod>(req.Method);
        return Respond(await brackets.AdvanceAsync(eventId, divisionId, req.MatchId, req.WinnerId, method, ct));
    }

    [HttpPost("divisions/{divisionId:long}/bracket:start")]
    public async Task<IActionResult> Start(long eventId, long divisionId, CancellationToken ct) =>
        Respond(await brackets.StartAsync(eventId, divisionId, ct));

    [HttpPost("divisions/{divisionId:long}:finalize")]
    public async Task<IActionResult> Finalize(long eventId, long divisionId, CancellationToken ct) =>
        Respond(await finalization.FinalizeAsync(eventId, divisionId, ct), p => Ok(p));

    [HttpGet("divisions/{divisionId:long}/standings")]
    public async Task<IActionResult> Standings(long eventId, long divisionId, CancellationToken ct)
    {
        var rows = await db.Standings.AsNoTracking().Where(s => s.DivisionId == divisionId)
            .OrderByDescending(s => s.Wins).ThenBy(s => s.Losses).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("disputes")]
    public async Task<IActionResult> Flag(long eventId, FlagDisputeRequest req, CancellationToken ct) =>
        Respond(await disputes.FlagAsync(eventId, req.DivisionId, req.MatchId, req.Reason, ct), id => Ok(new { disputeId = id }));

    [HttpPut("disputes/{disputeId:long}")]
    public async Task<IActionResult> Resolve(long eventId, long disputeId, ResolveDisputeRequest req, CancellationToken ct) =>
        Respond(await disputes.ResolveAsync(eventId, disputeId, req.Resolution, ct));

    [HttpPost("mat-assignments")]
    public async Task<IActionResult> AssignMat(long eventId, AssignMatRequest req, CancellationToken ct) =>
        Respond(await devices.AssignMatAsync(req.DeviceId, req.DivisionId, ct));
}
