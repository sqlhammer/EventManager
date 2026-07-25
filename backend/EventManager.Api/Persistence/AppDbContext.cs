using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Persistence;

/// <summary>
/// Single EF Core context spanning both persistence planes (Q1=C): ASP.NET Identity tables
/// (accounts/MFA/lockout) + the append-only event log + folded read-model tables + infra tables.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<long>, long>(options)
{
    public DbSet<EventRecord> Events => Set<EventRecord>();

    public DbSet<EventRow> EventRows => Set<EventRow>();
    public DbSet<DivisionRow> DivisionRows => Set<DivisionRow>();
    public DbSet<OrganizerRow> OrganizerRows => Set<OrganizerRow>();
    public DbSet<RegistrationRow> RegistrationRows => Set<RegistrationRow>();
    public DbSet<AthleteProfileRow> AthleteProfileRows => Set<AthleteProfileRow>();
    public DbSet<ResultRow> ResultRows => Set<ResultRow>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();
    public DbSet<EmailOutboxRecord> EmailOutbox => Set<EmailOutboxRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<EventRecord>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).ValueGeneratedNever();
            e.HasIndex(x => new { x.DeviceId, x.SequenceNumber }).IsUnique(); // idempotence (RP-1)
            e.HasIndex(x => x.EventScopeId);                                   // event-scoped reads/authz
        });

        b.Entity<EventRow>(e => { e.HasKey(x => x.EventId); e.Property(x => x.EntryFee).HasColumnType("numeric"); });
        b.Entity<DivisionRow>(e => { e.HasKey(x => x.DivisionId); e.HasIndex(x => x.EventId); });
        b.Entity<OrganizerRow>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.EventId, x.AccountId }).IsUnique(); });
        b.Entity<RegistrationRow>(e => { e.HasKey(x => x.RegistrationId); e.HasIndex(x => x.EventId); });
        b.Entity<AthleteProfileRow>(e => { e.HasKey(x => x.AthleteId); e.HasIndex(x => x.OwnerAccountId); });
        b.Entity<ResultRow>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.AthleteId); });

        b.Entity<IdempotencyKey>(e => e.HasKey(x => x.Key));
        b.Entity<RefreshTokenRecord>(e => { e.HasKey(x => x.TokenHash); e.HasIndex(x => x.AccountId); });
        b.Entity<EmailOutboxRecord>(e => e.HasKey(x => x.Id));

        b.Entity<AppUser>(e => e.HasIndex(x => x.AccountId).IsUnique());
    }
}
