using EventManager.Api.Events;
using EventManager.Api.Persistence;
using ErrorOr;
using Microsoft.AspNetCore.Identity;

namespace EventManager.Api.Services;

/// <summary>
/// Self-service account deletion (US-110). Re-authenticates the caller (password, plus TOTP when MFA
/// is enrolled), refuses deletion while the account is the sole Full Admin of any event, then
/// soft-deletes + anonymizes the identity record, detaches its organizer roles via the event log, and
/// revokes all refresh tokens. The <see cref="AppUser.AccountId"/> bridge is preserved so the
/// append-only event history stays consistent (the account plane is never event-sourced, Q1=C).
/// </summary>
public sealed class AccountDeletionService(
    UserManager<AppUser> users, AccountDeletionGuard guard, EventWriter writer, TokenService tokens, AppDbContext db)
{
    public async Task<ErrorOr<Success>> DeleteOwnAccountAsync(long accountId, string password, string? totp, CancellationToken ct = default)
    {
        var user = await FindByAccountIdAsync(accountId);
        if (user is null || user.DeletedAt is not null)
            return Error.NotFound("Account.NotFound", "Account not found.");

        // Re-authenticate: current password is mandatory; a TOTP is mandatory when MFA is enrolled.
        if (!await users.CheckPasswordAsync(user, password))
            return Error.Unauthorized("Account.Delete", "Invalid credentials.");

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrEmpty(totp))
                return Error.Unauthorized("Account.Mfa", "MFA code required.");
            var mfaOk = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, totp);
            if (!mfaOk)
                return Error.Unauthorized("Account.Mfa", "Invalid MFA code.");
        }

        // Block if the account is the sole Full Admin of any event — it must hand off first (BR-RBAC-3).
        var blocked = await guard.SoleFullAdminEventsAsync(accountId, ct);
        if (blocked.Count > 0)
        {
            var list = string.Join(", ", blocked);
            return Error.Conflict("Account.SoleFullAdmin",
                $"You are the only Full Admin of event(s): {list}. Add or promote another Full Admin before deleting your account.");
        }

        // Detach every organizer role through the append-only log. Each removal is safe because the
        // block above guarantees no event is left without a Full Admin.
        var roles = await guard.OrganizerRolesAsync(accountId, ct);
        foreach (var role in roles)
            await writer.AppendAsync(role.EventId, EventTypes.OrganizerRemoved,
                new OrganizerRemovedPayload(role.Id, role.EventId, accountId), ct);
        if (roles.Count > 0)
            await db.SaveChangesAsync(ct);

        // Soft-delete + anonymize; scrub credentials so the record can never authenticate again.
        Anonymize(user);
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
            return Error.Failure("Account.Delete", "Account deletion could not be completed.");

        // Revoke all refresh tokens so no existing session can rotate into a fresh access token.
        await tokens.LogoutAsync(accountId, ct);
        return Result.Success;
    }

    private void Anonymize(AppUser user)
    {
        var placeholder = $"deleted-{user.AccountId}@deleted.invalid";
        user.DeletedAt = DateTimeOffset.UtcNow;
        user.Email = placeholder;
        user.NormalizedEmail = users.NormalizeEmail(placeholder);
        user.EmailConfirmed = false;
        user.UserName = placeholder;
        user.NormalizedUserName = users.NormalizeName(placeholder);
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.PasswordHash = null;                     // no password can match
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.TwoFactorEnabled = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;    // belt-and-suspenders: permanently locked out
    }

    private async Task<AppUser?> FindByAccountIdAsync(long accountId)
    {
        foreach (var u in users.Users)
            if (u.AccountId == accountId) return await users.FindByIdAsync(u.Id.ToString());
        return null;
    }
}
