using EventManager.Domain.Engines;
using EventManager.Hub.Competition;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Hub.Projections;
using EventManager.Hub.Resilience;
using EventManager.Hub.Services;
using EventManager.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Tests;

/// <summary>In-memory SQLite wiring of the hub-core stack for service-level tests.</summary>
public sealed class HubTestHost : IDisposable
{
    private readonly SqliteConnection _conn;
    public HubDbContext Db { get; }
    public IIdGenerator Ids { get; }
    public IWorkerIdRegistry Workers { get; }
    public PairingService Pairing { get; }
    public DeviceRegistry Devices { get; }
    public SyncIntakeService Sync { get; }
    public OfflineOrganizerAuth Auth { get; }
    public HubEventStore Store { get; }
    public BracketService Brackets { get; }
    public ScoringIntakeService Scoring { get; }
    public WeighInResolutionService WeighIn { get; }
    public DivisionFinalizationService Finalization { get; }
    public DisputeService Disputes { get; }

    // ---- U10 replication adapter ----
    public ReplicationOptions ReplicationOptions { get; } = new();
    public ReplicationSignal Signal { get; } = new();
    public ReplicationStatus ReplicationStatus { get; } = new();
    public HubCredentialStore Credentials { get; }
    public FakeClock Clock { get; } = new();

    /// <summary>Controllable time so breaker cool-downs can be tested without sleeping.</summary>
    public sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    public HubTestHost()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<HubDbContext>().UseSqlite(_conn).Options;
        Db = new HubDbContext(options);
        Db.Database.EnsureCreated();

        Ids = new SnowflakeIdGenerator(workerId: 1);
        Workers = new WorkerIdRegistry();
        var ser = new JsonEventSerializer();
        var proj = new HubProjectionHost(Db, ser);
        var writer = new HubEventWriter(Db, Ids, ser, proj, Signal);
        Store = new HubEventStore(Db);
        var push = new InProcessHubPush();
        Pairing = new PairingService(Db, writer, Ids, Workers, new HubIdentity());
        Devices = new DeviceRegistry(Db, writer, Workers, push);
        Sync = new SyncIntakeService(Db, Store, Devices, Signal);
        Auth = new OfflineOrganizerAuth(Db, new RoleAuthorizationPolicy());

        Brackets = new BracketService(Db, writer, Ids, new SeedingEngine(), new BracketEngine());
        Scoring = new ScoringIntakeService(Devices, new ScoringEngine(), Brackets);
        WeighIn = new WeighInResolutionService(Db, writer, new WeighInPolicyEvaluator());
        Finalization = new DivisionFinalizationService(Db, writer);
        Disputes = new DisputeService(Db, writer, Ids);

        // Pass-through protection: DPAPI is machine- and user-bound, so a test could not read back
        // what another machine wrote. The seam exists precisely so this substitution is possible.
        Credentials = new HubCredentialStore(Db, new PassthroughSecretProtector(), ReplicationOptions);
    }

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}
