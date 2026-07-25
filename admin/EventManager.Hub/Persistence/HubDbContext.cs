using EventManager.Hub.Competition;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Persistence;

/// <summary>Hub-local store (SQLite via EF Core): event log + device/pairing/auth/readiness tables.</summary>
public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<HubEventRecord> Events => Set<HubEventRecord>();
    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();
    public DbSet<PairingTokenRecord> PairingTokens => Set<PairingTokenRecord>();
    public DbSet<OrganizerCredentialRecord> OrganizerCredentials => Set<OrganizerCredentialRecord>();
    public DbSet<ReadinessRecord> Readiness => Set<ReadinessRecord>();

    // U4b competition read models
    public DbSet<BracketRow> Brackets => Set<BracketRow>();
    public DbSet<StandingRow> Standings => Set<StandingRow>();
    public DbSet<DisputeRow> Disputes => Set<DisputeRow>();
    public DbSet<DivisionStatusRow> DivisionStatuses => Set<DivisionStatusRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<HubEventRecord>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).ValueGeneratedNever();
            e.HasIndex(x => new { x.DeviceId, x.SequenceNumber }).IsUnique();  // idempotent append
            e.HasIndex(x => x.EventScopeId);
        });
        b.Entity<DeviceRecord>(e => { e.HasKey(x => x.DeviceId); e.HasIndex(x => x.EventId); });
        b.Entity<PairingTokenRecord>(e => e.HasKey(x => x.Token));
        b.Entity<OrganizerCredentialRecord>(e => e.HasKey(x => new { x.AccountId, x.EventId }));
        b.Entity<ReadinessRecord>(e => e.HasKey(x => x.EventId));

        b.Entity<BracketRow>(e => { e.HasKey(x => x.DivisionId); e.HasIndex(x => x.EventId); });
        b.Entity<StandingRow>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.DivisionId); });
        b.Entity<DisputeRow>(e => { e.HasKey(x => x.DisputeId); e.HasIndex(x => x.DivisionId); });
        b.Entity<DivisionStatusRow>(e => e.HasKey(x => x.DivisionId));
    }
}
