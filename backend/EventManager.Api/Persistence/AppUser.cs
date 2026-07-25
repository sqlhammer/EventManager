using Microsoft.AspNetCore.Identity;

namespace EventManager.Api.Persistence;

/// <summary>
/// Identity-plane user (Q1=C). Credentials, MFA, lockout live in ASP.NET Identity tables and are
/// NEVER event-sourced. <see cref="AccountId"/> is the cloud-minted Snowflake that bridges to the
/// domain plane (organizer role assignments, registration ownership).
/// </summary>
public sealed class AppUser : IdentityUser<long>
{
    /// <summary>Cross-plane Snowflake account id referenced by domain events.</summary>
    public long AccountId { get; set; }
}
