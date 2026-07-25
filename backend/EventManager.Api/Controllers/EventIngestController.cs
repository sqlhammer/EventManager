using EventManager.Api.Auth;
using EventManager.Api.Services;
using EventManager.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Replication ingest (US-504). Event-scoped JWT authz (Q7=A); idempotent, sequence-ordered.</summary>
[Route("api/ingest")]
[Authorize]
public sealed class EventIngestController(IngestService ingest, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch(ReplicationBatchDto batch, CancellationToken ct) =>
        Respond(await ingest.IngestAsync(currentUser.RequireAccountId(), batch, ct), ack => Ok(ack));
}
