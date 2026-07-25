using EventManager.Api.Auth;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Registrant results & history (US-603). Object-level authz — owner/manager only.</summary>
[Route("api/results")]
[Authorize]
public sealed class ResultsController(ResultsQueryService results, CurrentUser currentUser) : ApiControllerBase
{
    [HttpGet("athletes/{athleteId:long}")]
    public async Task<IActionResult> GetForAthlete(long athleteId, CancellationToken ct) =>
        Respond(await results.GetForAthleteAsync(currentUser.RequireAccountId(), athleteId, ct), r => Ok(r));
}
