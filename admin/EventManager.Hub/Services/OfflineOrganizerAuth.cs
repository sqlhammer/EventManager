using System.Security.Cryptography;
using System.Text;
using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Hub.Persistence;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Services;

/// <summary>
/// Offline organizer authentication + hub-side RBAC (D-27, US-302 admin actions). Credentials +
/// role assignments are packaged at event download so the hub authorizes with zero internet, reusing
/// the SAME U1 <see cref="RoleAuthorizationPolicy"/> as the cloud — authorization cannot diverge.
/// </summary>
public sealed class OfflineOrganizerAuth(HubDbContext db, IRoleAuthorizationPolicy policy)
{
    /// <summary>Verify a packaged organizer credential offline. Returns the role on success.</summary>
    public async Task<ErrorOr<OrganizerRole>> VerifyAsync(long eventId, long accountId, string password, CancellationToken ct = default)
    {
        var cred = await db.OrganizerCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EventId == eventId && c.AccountId == accountId, ct);
        if (cred is null || cred.PasswordHash != Hash(password))
            return Error.Unauthorized("HubAuth.Invalid", "Invalid organizer credentials."); // non-enumerating
        return Enum.Parse<OrganizerRole>(cred.Role);
    }

    /// <summary>Deny-by-default hub authorization using the packaged role + the shared U1 policy.</summary>
    public async Task<bool> IsPermittedAsync(long accountId, long eventId, OrganizerAction action, CancellationToken ct = default)
    {
        var cred = await db.OrganizerCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EventId == eventId && c.AccountId == accountId, ct);
        var assignment = cred is null ? null
            : new OrganizerRoleAssignment((Snowflake)accountId, (Snowflake)eventId, (Snowflake)accountId, Enum.Parse<OrganizerRole>(cred.Role));
        return policy.IsPermitted(assignment, action);
    }

    /// <summary>Package an organizer credential (called during event download).</summary>
    public async Task PackageAsync(long eventId, long accountId, string role, string password, CancellationToken ct = default)
    {
        db.OrganizerCredentials.Add(new OrganizerCredentialRecord { EventId = eventId, AccountId = accountId, Role = role, PasswordHash = Hash(password) });
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
