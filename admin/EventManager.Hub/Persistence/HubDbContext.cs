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
    }
}
