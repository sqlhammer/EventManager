using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventManager.Api.Controllers;

/// <summary>Accounts & auth (US-101/102/103). Register/login/MFA + token refresh/logout.</summary>
[Route("api/accounts")]
public sealed class AccountController(AccountService accounts, TokenService tokens, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("registration")]
    public async Task<IActionResult> Register(RegisterAccountRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await accounts.RegisterAsync(req.Email, req.Password, ct);
        // Generic 200 on success; duplicate maps to a non-enumerating conflict message.
        return result.IsError ? Problem(result.Errors) : Ok(new { message = "Registration received. Check your email to confirm." });
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest req, CancellationToken ct) =>
        Respond(await accounts.ConfirmEmailAsync(req.Email, req.Token, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await accounts.LoginAsync(req.Email, req.Password, req.Totp, ct);
        return Respond(result, outcome => outcome.MfaRequired
            ? Ok(new { mfaRequired = true })
            : Ok(new TokenResponse(outcome.Tokens!.AccessToken, outcome.Tokens.RefreshToken, outcome.Tokens.AccessExpiresAt)));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest req, CancellationToken ct)
    {
        var issued = await tokens.RefreshAsync(req.RefreshToken, req.Email, ct);
        return issued is null
            ? Unauthorized()
            : Ok(new TokenResponse(issued.AccessToken, issued.RefreshToken, issued.AccessExpiresAt));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await accounts.LogoutAsync(currentUser.RequireAccountId(), ct);
        return Ok();
    }

    [HttpPost("mfa/enroll")]
    [Authorize]
    public async Task<IActionResult> EnrollMfa(CancellationToken ct) =>
        Respond(await accounts.BeginMfaEnrollmentAsync(currentUser.RequireAccountId(), ct),
            e => Ok(new MfaEnrollResponse(e.SharedKey, e.AuthenticatorUri, e.RecoveryCodes)));

    [HttpPost("mfa/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmMfa(MfaConfirmRequest req, CancellationToken ct) =>
        Respond(await accounts.ConfirmMfaAsync(currentUser.RequireAccountId(), req.Totp, ct));
}
