using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.AspNetCore.Identity;

namespace EventManager.Api.Services;

public sealed record LoginOutcome(bool MfaRequired, IssuedTokens? Tokens);
public sealed record MfaEnrollment(string SharedKey, string AuthenticatorUri, IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Identity-plane orchestration (US-101/102/103). Wraps ASP.NET Identity: registration with the
/// breached-password validator + email-confirmation gate, login with progressive lockout + MFA
/// challenge, TOTP enrollment. Non-enumerating responses (BR-AUTH-3) are shaped by the controller.
/// </summary>
public sealed class AccountService(
    UserManager<AppUser> users, EventManager.Sync.IIdGenerator ids, TokenService tokens, IEmailSender email)
{
    public async Task<ErrorOr<Success>> RegisterAsync(string emailAddress, string password, CancellationToken ct = default)
    {
        var existing = await users.FindByEmailAsync(emailAddress);
        if (existing is not null)
            return Error.Conflict("Account.Duplicate", "Registration could not be completed."); // non-enumerating

        var user = new AppUser { UserName = emailAddress, Email = emailAddress, AccountId = ids.NextId() };
        var result = await users.CreateAsync(user, password);   // runs BreachedPasswordValidator (SP-5)
        if (!result.Succeeded)
            return Error.Validation("Account.Password", string.Join("; ", result.Errors.Select(e => e.Description)));

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendConfirmationAsync(emailAddress, token, ct);   // BR-AUTH-4 gate until confirmed
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> ConfirmEmailAsync(string emailAddress, string token, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(emailAddress);
        if (user is null) return Error.NotFound("Account.NotFound", "Confirmation failed.");
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Result.Success : Error.Validation("Account.Confirm", "Confirmation failed.");
    }

    public async Task<ErrorOr<LoginOutcome>> LoginAsync(string emailAddress, string password, string? totp, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(emailAddress);
        if (user is null) return Error.Unauthorized("Account.Login", "Invalid credentials."); // non-enumerating

        if (await users.IsLockedOutAsync(user))
            return Error.Forbidden("Account.Lockout", "Account temporarily locked. Try again later.");

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);   // progressive lockout (BR-AUTH-2)
            return Error.Unauthorized("Account.Login", "Invalid credentials.");
        }
        await users.ResetAccessFailedCountAsync(user);

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrEmpty(totp)) return new LoginOutcome(MfaRequired: true, Tokens: null);
            var ok = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, totp);
            if (!ok) return Error.Unauthorized("Account.Mfa", "Invalid MFA code.");
        }

        var issued = await tokens.IssueAsync(user.AccountId, emailAddress, mfaSatisfied: true, ct);
        return new LoginOutcome(MfaRequired: false, Tokens: issued);
    }

    public async Task<ErrorOr<MfaEnrollment>> BeginMfaEnrollmentAsync(long accountId, CancellationToken ct = default)
    {
        var user = await FindByAccountIdAsync(accountId);
        if (user is null) return Error.NotFound("Account.NotFound", "Account not found.");
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }
        var recovery = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? [];
        var uri = $"otpauth://totp/EventManager:{user.Email}?secret={key}&issuer=EventManager";
        return new MfaEnrollment(key!, uri, recovery.ToList());
    }

    public async Task<ErrorOr<Success>> ConfirmMfaAsync(long accountId, string totp, CancellationToken ct = default)
    {
        var user = await FindByAccountIdAsync(accountId);
        if (user is null) return Error.NotFound("Account.NotFound", "Account not found.");
        var ok = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, totp);
        if (!ok) return Error.Validation("Account.Mfa", "Invalid MFA code.");
        await users.SetTwoFactorEnabledAsync(user, true);
        return Result.Success;
    }

    public async Task LogoutAsync(long accountId, CancellationToken ct = default) => await tokens.LogoutAsync(accountId, ct);

    /// <summary>Email-confirmation gate for event creation (BR-AUTH-4).</summary>
    public async Task<bool> IsEmailConfirmedAsync(long accountId)
    {
        var user = await FindByAccountIdAsync(accountId);
        return user is not null && await users.IsEmailConfirmedAsync(user);
    }

    private async Task<AppUser?> FindByAccountIdAsync(long accountId)
    {
        foreach (var u in users.Users)
            if (u.AccountId == accountId) return await users.FindByIdAsync(u.Id.ToString());
        return null;
    }
}
