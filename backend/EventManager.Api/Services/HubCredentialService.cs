using System.Security.Cryptography;
using System.Text;
using EventManager.Api.Auth;
using EventManager.Api.Persistence;
using EventManager.Domain;
using EventManager.Sync;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>Credential policy knobs (BR-REPL-4, BR-REPL-5). Bound from configuration.</summary>
public sealed class HubCredentialOptions
{
    /// <summary>Days after the event date that a credential stays usable (FD-Q1=C + CL-B=D).</summary>
    public int GraceDays { get; set; } = 14;

    /// <summary>Maximum credentials in state Active per event (FD-Q2 = "C, the cap is 3").</summary>
    public int MaxActivePerEvent { get; set; } = 3;
}

/// <summary>Returned exactly once, at issue. The only shape that ever carries <see cref="Key"/>.</summary>
public sealed record IssuedHubCredential(long CredentialId, string Key, long EventScopeId, DateTimeOffset ExpiresAt);

/// <summary>Listing shape — deliberately carries no key material (BR-REPL-24).</summary>
public sealed record HubCredentialSummary(
    long CredentialId, string Label, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt, string State);

/// <summary>
/// U10 hub-credential lifecycle: issue, authenticate, revoke, list (US-801, US-808).
/// This is the unit's security-critical module and is deliberately isolated (SECURITY-11).
///
/// The key is 256 bits of CSPRNG output returned ONCE (BR-REPL-2); only a SHA-256 hash is persisted
/// (BR-REPL-3), matching <see cref="RefreshTokenStore"/>. The hash is deliberately unsalted: a salted
/// hash cannot be looked up, so authentication would have to scan every row, and salting exists to
/// defeat rainbow tables against low-entropy secrets — which a 256-bit random key is not.
/// </summary>
public sealed class HubCredentialService(
    AppDbContext db, EventAuthorizer authz, IIdGenerator ids, HubCredentialOptions options, TimeProvider clock)
{
    /// <summary>Issue a credential for an event (BR-REPL-1..6).</summary>
    public async Task<ErrorOr<IssuedHubCredential>> IssueAsync(
        long issuerAccountId, long eventScopeId, string label, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(issuerAccountId, eventScopeId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("HubCredential.Scope", "Not authorized to issue credentials for this event.");

        if (string.IsNullOrWhiteSpace(label))
            return Error.Validation("HubCredential.Label", "A label is required.");
        if (label.Length > 120)
            return Error.Validation("HubCredential.Label", "Label must be 120 characters or fewer.");

        var eventRow = await db.EventRows.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventScopeId, ct);
        if (eventRow is null) return Error.NotFound("HubCredential.Event", "Event not found.");

        var now = clock.GetUtcNow();
        var active = await CountActiveAsync(eventScopeId, now, ct);
        if (active >= options.MaxActivePerEvent)
            return Error.Conflict("HubCredential.Cap",
                $"This event already has {options.MaxActivePerEvent} active hub credentials. Revoke one before issuing another.");

        var key = NewKey();
        var record = new HubCredentialRecord
        {
            CredentialId = ids.NextId(),
            EventScopeId = eventScopeId,
            KeyHash = Hash(key),
            Label = label,
            IssuedByAccountId = issuerAccountId,
            IssuedAt = now,
            ExpiresAt = ExpiryFor(eventRow.Date, now),
        };
        db.HubCredentials.Add(record);
        await db.SaveChangesAsync(ct);

        // The only time the key leaves this method. It is not stored and cannot be retrieved again.
        return new IssuedHubCredential(record.CredentialId, key, record.EventScopeId, record.ExpiresAt);
    }

    /// <summary>
    /// Resolve a presented key to an active credential (BR-REPL-7, BR-REPL-8). Evaluated on every
    /// request with no session or cache, which is what makes revocation effective immediately.
    /// Returns null for every failure mode so callers cannot distinguish unknown from revoked.
    /// </summary>
    public async Task<HubCredentialPrincipal?> AuthenticateAsync(string presentedKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey)) return null;

        var hash = Hash(presentedKey);
        var row = await db.HubCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.KeyHash == hash, ct);
        if (row is null) return null;
        if (!row.IsActive(clock.GetUtcNow())) return null;   // expired and revoked are identical here (BR-REPL-14)

        return new HubCredentialPrincipal(row.CredentialId, row.EventScopeId);
    }

    /// <summary>Revoke immediately and irreversibly (BR-REPL-15).</summary>
    public async Task<ErrorOr<Success>> RevokeAsync(
        long callerAccountId, long eventScopeId, long credentialId, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(callerAccountId, eventScopeId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("HubCredential.Scope", "Not authorized to manage credentials for this event.");

        var row = await db.HubCredentials.FirstOrDefaultAsync(
            c => c.CredentialId == credentialId && c.EventScopeId == eventScopeId, ct);
        if (row is null) return Error.NotFound("HubCredential.NotFound", "Credential not found.");

        if (row.RevokedAt is null)
        {
            row.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return Result.Success;   // idempotent: revoking an already-revoked credential is not an error
    }

    /// <summary>List credentials for an event, without key material (BR-REPL-24).</summary>
    public async Task<ErrorOr<IReadOnlyList<HubCredentialSummary>>> ListAsync(
        long callerAccountId, long eventScopeId, CancellationToken ct = default)
    {
        if (!await authz.IsPermittedAsync(callerAccountId, eventScopeId, OrganizerAction.ManageRoster, ct))
            return Error.Forbidden("HubCredential.Scope", "Not authorized to view credentials for this event.");

        var now = clock.GetUtcNow();
        // Ordering is client-side for the same reason as CountActiveAsync: SQLite cannot sort by
        // DateTimeOffset, and these services stay provider-agnostic so they can be tested on it.
        var rows = await db.HubCredentials.AsNoTracking()
            .Where(c => c.EventScopeId == eventScopeId)
            .ToListAsync(ct);
        rows = [.. rows.OrderByDescending(c => c.IssuedAt)];

        var summaries = new List<HubCredentialSummary>(rows.Count);
        foreach (var r in rows)
            summaries.Add(new HubCredentialSummary(r.CredentialId, r.Label, r.IssuedAt, r.ExpiresAt, r.RevokedAt, StateOf(r, now)));
        return summaries;
    }

    /// <summary>Expiry = event date + grace (BR-REPL-4). Never caller-supplied.</summary>
    private DateTimeOffset ExpiryFor(DateOnly eventDate, DateTimeOffset now)
    {
        var baseline = new DateTimeOffset(eventDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var expiry = baseline.AddDays(options.GraceDays);
        // An event already in the past would otherwise yield a credential that is dead on arrival.
        if (expiry <= now) return now.AddDays(options.GraceDays);
        return expiry;
    }

    /// <summary>
    /// Counts credentials in state Active. The expiry comparison is evaluated client-side because
    /// SQLite cannot translate <see cref="DateTimeOffset"/> comparisons, and services in this repo are
    /// kept provider-agnostic so they can be tested on SQLite — the same approach
    /// <see cref="RefreshTokenStore"/> takes. The row count per event is bounded by the cap, so this
    /// is a handful of rows.
    /// </summary>
    private async Task<int> CountActiveAsync(long eventScopeId, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await db.HubCredentials.AsNoTracking()
            .Where(c => c.EventScopeId == eventScopeId && c.RevokedAt == null)
            .Select(c => c.ExpiresAt)
            .ToListAsync(ct);

        var active = 0;
        foreach (var expiresAt in candidates)
        {
            if (expiresAt > now) active++;
        }
        return active;
    }

    private static string StateOf(HubCredentialRecord r, DateTimeOffset now)
    {
        if (r.IsRevoked) return "Revoked";
        if (r.IsExpired(now)) return "Expired";
        return "Active";
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
