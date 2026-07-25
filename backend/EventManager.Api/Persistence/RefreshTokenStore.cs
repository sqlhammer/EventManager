using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Persistence;

/// <summary>
/// Refresh-token persistence with rotation + revocation (SP-1, BR-AUTH-6). Only token hashes are
/// stored. Logout revokes; rotation-on-use revokes the old and links the replacement.
/// </summary>
public sealed class RefreshTokenStore(AppDbContext db)
{
    public async Task IssueAsync(string rawToken, long accountId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(new RefreshTokenRecord { TokenHash = Hash(rawToken), AccountId = accountId, ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns the account id if the token is currently valid (exists, not revoked, not expired).</summary>
    public async Task<long?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == Hash(rawToken), ct);
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        return row.AccountId;
    }

    public async Task<bool> RotateAsync(string oldRaw, string newRaw, DateTimeOffset newExpiresAt, CancellationToken ct = default)
    {
        var row = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == Hash(oldRaw), ct);
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= DateTimeOffset.UtcNow) return false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.ReplacedByHash = Hash(newRaw);
        db.RefreshTokens.Add(new RefreshTokenRecord { TokenHash = row.ReplacedByHash, AccountId = row.AccountId, ExpiresAt = newExpiresAt });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevokeAllForAccountAsync(long accountId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(x => x.AccountId == accountId && x.RevokedAt == null)
            .ForEachAsync(x => x.RevokedAt = now, ct);
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
