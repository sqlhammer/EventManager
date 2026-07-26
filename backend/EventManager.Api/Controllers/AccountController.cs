using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventManager.Api.Controllers;

/// <summary>Accounts & auth (US-101/102/103). Register/login/MFA + token refresh/logout.</summary>
[Route("api/accounts")]
public sealed class AccountController(AccountService accounts, AccountDeletionService deletion, TokenService tokens, CurrentUser currentUser) : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("registration")]
    public async Task<IActionResult> Register(RegisterAccountRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        var result = await accounts.RegisterAsync(req.Email, req.Password, ct);
        // Generic 200 on success; duplicate maps to a non-enumerating conflict message.
        if (result.IsError) return Problem(result.Errors);
        return Ok(new { message = "Registration received. Check your email to confirm." });
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
        return Respond(result, outcome =>
        {
            if (outcome.MfaRequired) return Ok(new { mfaRequired = true });
            return Ok(new TokenResponse(outcome.Tokens!.AccessToken, outcome.Tokens.RefreshToken, outcome.Tokens.AccessExpiresAt));
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest req, CancellationToken ct)
    {
        var issued = await tokens.RefreshAsync(req.RefreshToken, req.Email, ct);
        if (issued is null) return Unauthorized();
        return Ok(new TokenResponse(issued.AccessToken, issued.RefreshToken, issued.AccessExpiresAt));
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

    /// <summary>Self-service account deletion (US-110). Soft-deletes + anonymizes the caller's own
    /// account after re-authentication; refused while they are the sole Full Admin of any event.</summary>
    [HttpDelete("me")]
    [Authorize]
    public async Task<IActionResult> DeleteMe(DeleteAccountRequest req, CancellationToken ct)
    {
        if (await ValidateAsync(req, ct) is { } bad) return bad;
        return Respond(await deletion.DeleteOwnAccountAsync(currentUser.RequireAccountId(), req.Password, req.Totp, ct));
    }
}
