using EventManager.Api.Auth;
using EventManager.Api.Events;
using EventManager.Api.Persistence;
using EventManager.Api.Projections;
using EventManager.Api.Services;
using EventManager.Domain.Engines;
using EventManager.Payments;
using EventManager.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Tests;

/// <summary>
/// In-memory (SQLite) wiring of the U3 stack for service-level tests. The event store and projections
/// are provider-agnostic, so the real components run unchanged against SQLite.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _conn;
    public AppDbContext Db { get; }
    public EventWriter Writer { get; }
    public CloudProjectionHost Projections { get; }
    public EventAuthorizer Authorizer { get; }
    public RegistrationService Registrations { get; }
    public EventService Events { get; }
    public IngestService Ingest { get; }
    public IIdGenerator Ids { get; }
    public StubPaymentProvider Payments { get; }

    public TestHost(Func<PaymentRequest, PaymentOutcome>? paymentOutcome = null)
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();

        Ids = new SnowflakeIdGenerator(workerId: 0);
        var serializer = new JsonEventSerializer();
        Projections = new CloudProjectionHost(Db, serializer);
        Writer = new EventWriter(Db, Ids, serializer, Projections);
        Payments = new StubPaymentProvider(paymentOutcome);
        var idempotency = new IdempotencyStore(Db);
        Authorizer = new EventAuthorizer(Db, new RoleAuthorizationPolicy());
        Registrations = new RegistrationService(Db, Writer, Ids, idempotency, Payments, Authorizer);
        Events = new EventService(Db, Writer, Ids, Authorizer);
        Ingest = new IngestService(Db, Projections, Authorizer);
    }

    /// <summary>Seed an open event with a single division and one owned athlete profile. Returns ids.</summary>
    public async Task<(long EventId, long DivisionId, long AthleteId, long AccountId)> SeedOpenEventAsync(
        double weightUpper = 100, string gender = "M", int age = 25)
    {
        long accountId = Ids.NextId();
        var create = await Events.CreateEventAsync(accountId, new CreateEventInput(
            "Test Open", "Dojo", new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20),
            50m, nameof(EventManager.Domain.WeighInPolicyMode.Strict), null));
        var eventId = create.Value;
        await Events.SetRegistrationOpenAsync(accountId, eventId, true);
        var div = await Events.ConfigureDivisionAsync(accountId, new ConfigureDivisionInput(
            eventId, null, weightUpper, 0, 100, 0, 120, gender, nameof(EventManager.Domain.BracketFormat.SingleElimination)));
        var athleteId = await Registrations.UpsertProfileAsync(accountId, null, new ProfileInput(
            "Athlete", new DateOnly(2026, 1, 1).AddYears(-age), 5, weightUpper - 10, "Academy A", gender));
        return (eventId, div.Value, athleteId.Value, accountId);
    }

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}
