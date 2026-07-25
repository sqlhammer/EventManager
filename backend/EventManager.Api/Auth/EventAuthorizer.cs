using System.Security.Claims;
using EventManager.Api.Persistence;
using EventManager.Api.Services;
using EventManager.Domain;
using EventManager.Domain.Engines;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Auth;

/// <summary>Reads the authenticated account id from the JWT (SP-2).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public long? AccountId
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirstValue(TokenService.AccountIdClaim);
            if (long.TryParse(raw, out var id)) return id;
            return null;
        }
    }

    public long RequireAccountId() => AccountId ?? throw new UnauthorizedAccessException("No authenticated account.");
}

/// <summary>
/// Deny-by-default event RBAC (SP-2, BR-RBAC-*). Resolves the caller's <see cref="OrganizerRoleAssignment"/>
/// for the target event and delegates the decision to the SAME U1 <see cref="RoleAuthorizationPolicy"/>
/// the hub uses — so cloud and hub authorization cannot diverge.
/// </summary>
public sealed class EventAuthorizer(AppDbContext db, IRoleAuthorizationPolicy policy)
{
    public async Task<bool> IsPermittedAsync(long accountId, long eventId, OrganizerAction action, CancellationToken ct = default)
    {
        var row = await db.OrganizerRows.AsNoTracking()
            .FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == accountId, ct);
        OrganizerRoleAssignment? assignment = null;
        if (row is not null)
            assignment = new OrganizerRoleAssignment((Snowflake)row.Id, (Snowflake)row.EventId, (Snowflake)row.AccountId,
                Enum.Parse<OrganizerRole>(row.Role));
        return policy.IsPermitted(assignment, action);   // deny-by-default when assignment is null
    }

    /// <summary>True iff the account holds Full Admin on the event (gates add-organizer, US-108/BR-RBAC-1).</summary>
    public async Task<bool> IsFullAdminAsync(long accountId, long eventId, CancellationToken ct = default)
    {
        var row = await db.OrganizerRows.AsNoTracking()
            .FirstOrDefaultAsync(o => o.EventId == eventId && o.AccountId == accountId, ct);
        return row is not null && row.Role == nameof(OrganizerRole.FullAdmin);
    }
}
