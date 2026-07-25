using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Registration (US-201–207, 209–211). Self/parent/bulk registration, edits, roster mgmt.</summary>
[Route("api/registration")]
[Authorize]
public sealed class RegistrationController(RegistrationService registrations, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost("profiles")]
    public async Task<IActionResult> UpsertProfile(ProfileRequest req, [FromQuery] long? athleteId, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await registrations.UpsertProfileAsync(currentUser.RequireAccountId(), athleteId,
            new ProfileInput(req.Name, req.DateOfBirth, req.Rank, req.Weight, req.Academy, req.Gender), ct);
        return Respond(result, id => Ok(new IdResponse(id)));
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await registrations.RegisterAsync(currentUser.RequireAccountId(),
            new RegisterInput(req.EventId, req.AthleteId, req.DivisionIds, req.PayByCard), ct);
        return Respond(result, r => Ok(r));
    }

    [HttpPost("batch")]
    public async Task<IActionResult> RegisterBatch(BatchRegisterRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var entries = req.Entries.Select(e => new BatchEntry(e.AthleteId, e.DivisionIds)).ToList();
        var result = await registrations.RegisterBatchAsync(currentUser.RequireAccountId(),
            new BatchRegisterInput(req.EventId, entries, req.PayByCard, req.IdempotencyKey), ct);
        return Respond(result, r => Ok(r));
    }

    [HttpPut("{registrationId:long}")]
    public async Task<IActionResult> Edit(long registrationId, EditRegistrationRequest req, CancellationToken ct)
    {
        var result = await registrations.EditAsync(currentUser.RequireAccountId(), registrationId, req.DivisionIds, ct);
        return Respond(result, r => Ok(r));
    }

    [HttpDelete("{registrationId:long}")]
    public async Task<IActionResult> Withdraw(long registrationId, CancellationToken ct) =>
        Respond(await registrations.WithdrawAsync(currentUser.RequireAccountId(), registrationId, ct));

    [HttpPut("{registrationId:long}/payment-status")]
    public async Task<IActionResult> SetPaymentStatus(long registrationId, PaymentStatusRequest req, CancellationToken ct) =>
        Respond(await registrations.SetPaymentStatusAsync(currentUser.RequireAccountId(), registrationId, req.Status, ct));
}
