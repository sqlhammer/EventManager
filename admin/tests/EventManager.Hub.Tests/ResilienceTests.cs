using EventManager.ClientSync;
using EventManager.Hub.Persistence;
using EventManager.Hub.Resilience;
using EventManager.Sync;
using FsCheck.Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Tests;

/// <summary>U7 offline-resilience integration tests: hub→cloud replication + outage replay,
/// completeness (US-602), backup/restore (US-505/506), the zero-internet property (US-501),
/// and spoke offline-queue drain (US-502/503).</summary>
public sealed class ResilienceTests
{
    /// <summary>An in-memory SQLite event store standing in for a hub or the cloud mirror.</summary>
    private sealed class Store : IDisposable
    {
        private readonly SqliteConnection _conn;
        public HubDbContext Db { get; }
        public HubEventStore Events { get; }

        public Store()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            Db = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseSqlite(_conn).Options);
            Db.Database.EnsureCreated();
            Events = new HubEventStore(Db);
        }

        public async Task SeedAsync(long deviceId, int count)
        {
            for (int i = 1; i <= count; i++)
            {
                var evt = new TournamentEvent(deviceId * 100_000 + i, deviceId, i, "MatchScored", 1,
                    new byte[] { (byte)i }, DateTimeOffset.UtcNow, EventScopeId: 1);
                await Events.AppendIfNotExistsAsync(evt);
            }
            await Db.SaveChangesAsync();
        }

        public Task<long> HighWaterMarkAsync(long deviceId) => Events.HighWaterMarkAsync(deviceId);
        public int Count(long deviceId) => Db.Events.Count(e => e.DeviceId == deviceId);
        public void Dispose() { Db.Dispose(); _conn.Dispose(); }
    }

    [Fact]
    public async Task Outage_then_reconnect_replicates_all_exactly_once() // US-501/504/602
    {
        using var hub = new Store();
        using var cloud = new Store();
        await hub.SeedAsync(deviceId: 55, count: 6);

        var transport = new StoreBackedReplicationTransport(cloud.Db, cloud.Events) { IsOnline = false };
        var client = new ReplicationClient(hub.Events, new ReplicationProtocol(), transport);

        // Offline: replication is a no-op, cloud stays empty (hub keeps running — zero-internet).
        var offline = await client.ReplicateAsync();
        Assert.False(offline.Attempted);
        Assert.Equal(0, cloud.Count(55));

        // Reconnect: everything replicates.
        transport.IsOnline = true;
        var online = await client.ReplicateAsync();
        Assert.Equal(6, cloud.Count(55));

        // Re-run is idempotent — no duplicates.
        var rerun = await client.ReplicateAsync();
        Assert.Equal(0, rerun.EventsReplicated);
        Assert.Equal(6, cloud.Count(55));

        var completeness = await client.VerifyCompletenessAsync();
        Assert.True(completeness.IsComplete);
    }

    [Fact]
    public async Task Backup_and_restore_round_trips_the_log() // US-505/506
    {
        using var hub = new Store();
        await hub.SeedAsync(55, 5);
        await hub.SeedAsync(77, 3);

        var snapshot = await new BackupService().ExportAsync(hub.Events, "correct horse battery");

        using var recovered = new Store();
        var result = await new RecoveryService().RestoreAsync(snapshot, "correct horse battery", recovered.Events, () => recovered.Db.SaveChangesAsync());

        Assert.Equal(8, result.EventsRestored);
        Assert.Equal(5, recovered.Count(55));
        Assert.Equal(3, recovered.Count(77));
    }

    [Fact]
    public async Task Tampered_backup_fails_integrity() // US-505 integrity
    {
        using var hub = new Store();
        await hub.SeedAsync(55, 2);
        var snapshot = await new BackupService().ExportAsync(hub.Events, "pass");
        snapshot[^1] ^= 0xFF; // flip a byte in the ciphertext

        using var recovered = new Store();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new RecoveryService().RestoreAsync(snapshot, "pass", recovered.Events, () => recovered.Db.SaveChangesAsync()));
    }

    [Property(MaxTest = 40)] // US-501 zero-internet full-event property
    public void Zero_internet_then_sync_mirrors_every_event(byte rawCount)
    {
        int count = 1 + (rawCount % 20);
        using var hub = new Store();
        using var cloud = new Store();
        hub.SeedAsync(55, count).GetAwaiter().GetResult();

        // Cloud offline the entire time events are produced — hub is complete and independent.
        var transport = new StoreBackedReplicationTransport(cloud.Db, cloud.Events) { IsOnline = false };
        var client = new ReplicationClient(hub.Events, new ReplicationProtocol(), transport);
        client.ReplicateAsync().GetAwaiter().GetResult();
        Assert.Equal(count, hub.HighWaterMarkAsync(55).GetAwaiter().GetResult());
        Assert.Equal(0, cloud.Count(55));

        // Reconnect → every event mirrored exactly once.
        transport.IsOnline = true;
        client.ReplicateAsync().GetAwaiter().GetResult();
        Assert.Equal(hub.HighWaterMarkAsync(55).GetAwaiter().GetResult(), cloud.HighWaterMarkAsync(55).GetAwaiter().GetResult());
        Assert.Equal(count, cloud.Count(55));
    }

    [Fact]
    public async Task Spoke_offline_queue_drains_on_reconnect() // US-502/503 (U2 LocalEventQueue integration)
    {
        using var spoke = new Store();
        var queue = new LocalEventQueue(spoke.Events);
        for (int i = 1; i <= 4; i++)
        {
            await queue.EnqueueDurableAsync(new TournamentEvent(900_000 + i, 88, i, "CheckIn", 1, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, 1));
        }
        await spoke.Db.SaveChangesAsync();

        var pending = await queue.PendingAsync();
        Assert.Equal(4, pending.Count);                 // durable while offline — nothing lost

        queue.MarkAcked(new Dictionary<long, long> { [88] = 4 });   // hub acked on reconnect
        Assert.Equal(0, await queue.QueuedCountAsync());            // queue drains
    }
}
