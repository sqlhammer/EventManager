using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventManager.Api.Controllers;

/// <summary>Event & division setup (US-104/105/106/107). All actions require an authenticated organizer.</summary>
[Route("api/events")]
[Authorize]
public sealed class EventController(EventService events, AccountService accounts, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateEventRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var accountId = currentUser.RequireAccountId();
        if (!await accounts.IsEmailConfirmedAsync(accountId))   // BR-AUTH-4
            return Problem(detail: "Confirm your email before creating an event.", statusCode: StatusCodes.Status403Forbidden, title: "Account.Unconfirmed");

        var result = await events.CreateEventAsync(accountId,
            new CreateEventInput(req.Name, req.Venue, req.Date, req.RegistrationStart, req.RegistrationEnd,
                req.EntryFee, req.WeighInPolicyMode, req.WeighInTolerancePercent), ct);
        return Respond(result, id => Ok(new IdResponse(id)));
    }

    [HttpPut("{eventId:long}")]
    public async Task<IActionResult> Edit(long eventId, EditEventRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        return Respond(await events.EditEventAsync(currentUser.RequireAccountId(), eventId,
            req.Name, req.Venue, req.Date, req.RegistrationStart, req.RegistrationEnd, req.EntryFee, ct));
    }

    [HttpPost("{eventId:long}/registration/open")]
    public async Task<IActionResult> Open(long eventId, CancellationToken ct) =>
        Respond(await events.SetRegistrationOpenAsync(currentUser.RequireAccountId(), eventId, true, ct));

    [HttpPost("{eventId:long}/registration/close")]
    public async Task<IActionResult> Close(long eventId, CancellationToken ct) =>
        Respond(await events.SetRegistrationOpenAsync(currentUser.RequireAccountId(), eventId, false, ct));

    [HttpPut("{eventId:long}/payment-options")]
    public async Task<IActionResult> PaymentOptions(long eventId, PaymentOptionsRequest req, CancellationToken ct) =>
        Respond(await events.SetPaymentOptionsAsync(currentUser.RequireAccountId(), eventId, req.CardEnabled, ct));

    [HttpPut("{eventId:long}/weigh-in-policy")]
    public async Task<IActionResult> WeighInPolicy(long eventId, WeighInPolicyRequest req, CancellationToken ct) =>
        Respond(await events.SetWeighInPolicyAsync(currentUser.RequireAccountId(), eventId, req.Mode, req.TolerancePercent, ct));

    [HttpPost("{eventId:long}/divisions")]
    public async Task<IActionResult> ConfigureDivision(long eventId, ConfigureDivisionRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await events.ConfigureDivisionAsync(currentUser.RequireAccountId(),
            new ConfigureDivisionInput(eventId, req.WeightLower, req.WeightUpper, req.MinRank, req.MaxRank,
                req.MinAge, req.MaxAge, req.Gender, req.Format), ct);
        return Respond(result, id => Ok(new IdResponse(id)));
    }
}
