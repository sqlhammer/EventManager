using EventManager.Domain.Engines;
using EventManager.Hub.Competition;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Hub.Projections;
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
        var writer = new HubEventWriter(Db, Ids, ser, proj);
        Store = new HubEventStore(Db);
        var push = new InProcessHubPush();
        Pairing = new PairingService(Db, writer, Ids, Workers, new HubIdentity());
        Devices = new DeviceRegistry(Db, writer, Workers, push);
        Sync = new SyncIntakeService(Db, Store, Devices);
        Auth = new OfflineOrganizerAuth(Db, new RoleAuthorizationPolicy());

        Brackets = new BracketService(Db, writer, Ids, new SeedingEngine(), new BracketEngine());
        Scoring = new ScoringIntakeService(Devices, new ScoringEngine(), Brackets);
        WeighIn = new WeighInResolutionService(Db, writer, new WeighInPolicyEvaluator());
        Finalization = new DivisionFinalizationService(Db, writer);
        Disputes = new DisputeService(Db, writer, Ids);
    }

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}
