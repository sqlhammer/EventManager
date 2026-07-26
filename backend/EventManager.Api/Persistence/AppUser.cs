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

    /// <summary>
    /// Set when the user self-deletes their account (US-110). Non-null means the account is
    /// soft-deleted + PII-anonymized: login is refused and the credential fields are scrubbed, while
    /// the <see cref="AccountId"/> bridge is retained so the immutable event log stays consistent.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
