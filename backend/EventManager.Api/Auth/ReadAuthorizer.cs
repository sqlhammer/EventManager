using EventManager.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Auth;

/// <summary>
/// Read access tiers (U9). Totally ordered and cumulative: a caller holds the highest tier they
/// qualify for on an event, and it confers every lower tier's grants (BR-READ-2).
/// </summary>
public enum AccessTier
{
    None = 0,
    Public = 1,
    Registrant = 2,
    Organizer = 3,
}

/// <summary>
/// API-local read authorization (U9-CON-1). Deliberately does NOT extend the shared U1
/// <c>OrganizerAction</c> policy: tiers Public and Registrant are not organizer roles at all, and
/// extending the shared enum would reach <c>admin/EventManager.Hub</c>'s OfflineOrganizerAuth.
///
/// This is the single place a read tier is decided (SECURITY-11 separation of concerns). Query
/// services take an already-resolved tier and re-check it, so no single control is the sole
/// line of defence.
/// </summary>
public sealed class ReadAuthorizer(AppDbContext db)
{
    /// <summary>Resolve the caller's tier on one event (BR-READ-3/4/5). Three indexed lookups at worst.</summary>
    public async Task<AccessTier> ResolveAsync(long callerAccountId, long eventId, CancellationToken ct = default)
    {
        var row = await db.EventRows.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (row is null) return AccessTier.None;

        var isOrganizer = await db.OrganizerRows.AsNoTracking()
            .AnyAsync(o => o.EventId == eventId && o.AccountId == callerAccountId, ct);
        if (isOrganizer) return AccessTier.Organizer;

        var isRegistrant = await db.RegistrationRows.AsNoTracking()
            .AnyAsync(r => r.EventId == eventId && r.ManagedByAccountId == callerAccountId && !r.Withdrawn, ct);
        if (isRegistrant) return AccessTier.Registrant;

        // BR-READ-5: discoverability keys off status ONLY. An event left Open past its registration
        // window stays discoverable by design (Q4=C); the window is returned in the payload so a
        // client can present it as expired without the API concealing the event.
        if (row.RegistrationStatus == RegistrationStatusRow.Open) return AccessTier.Public;

        return AccessTier.None;
    }

    /// <summary>
    /// Resolve tiers for every event the caller can see, in four queries independent of result size
    /// (U9-NFR-9 — no N+1). Returns only events with a tier above <see cref="AccessTier.None"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, AccessTier>> ResolveAllAsync(long callerAccountId, CancellationToken ct = default)
    {
        var organizerIds = await db.OrganizerRows.AsNoTracking()
            .Where(o => o.AccountId == callerAccountId).Select(o => o.EventId).Distinct().ToListAsync(ct);

        var registrantIds = await db.RegistrationRows.AsNoTracking()
            .Where(r => r.ManagedByAccountId == callerAccountId && !r.Withdrawn)
            .Select(r => r.EventId).Distinct().ToListAsync(ct);

        var publicIds = await db.EventRows.AsNoTracking()
            .Where(e => e.RegistrationStatus == RegistrationStatusRow.Open).Select(e => e.EventId).ToListAsync(ct);

        var tiers = new Dictionary<long, AccessTier>();
        foreach (var id in publicIds) tiers[id] = AccessTier.Public;
        foreach (var id in registrantIds) tiers[id] = AccessTier.Registrant;
        foreach (var id in organizerIds) tiers[id] = AccessTier.Organizer;
        return tiers;
    }

    /// <summary>The caller's organizer role on an event, or null when they hold none.</summary>
    public async Task<string?> OrganizerRoleAsync(long callerAccountId, long eventId, CancellationToken ct = default)
    {
        var row = await db.OrganizerRows.AsNoTracking()
            .FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == callerAccountId, ct);
        if (row is null) return null;
        return row.Role;
    }

    /// <summary>Organizer roles for many events at once — one query, for the collection endpoint.</summary>
    public async Task<IReadOnlyDictionary<long, string>> OrganizerRolesAsync(long callerAccountId, CancellationToken ct = default)
    {
        var rows = await db.OrganizerRows.AsNoTracking()
            .Where(o => o.AccountId == callerAccountId).Select(o => new { o.EventId, o.Role }).ToListAsync(ct);
        var map = new Dictionary<long, string>();
        foreach (var row in rows) map[row.EventId] = row.Role;
        return map;
    }
}
