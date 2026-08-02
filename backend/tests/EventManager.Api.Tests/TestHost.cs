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

    // ---- U9 read/query components ----
    public ReadAuthorizer ReadAuth { get; }
    public ReadEtagProvider Etags { get; }
    public EventQueryService EventQueries { get; }
    public DivisionQueryService DivisionQueries { get; }
    public WeighInPolicyQueryService PolicyQueries { get; }
    public RegistrantQueryService RegistrantQueries { get; }
    public OrganizerAccountQueryService AccountQueries { get; }
    public OrganizerRoleService OrganizerRoles { get; }

    // ---- U10 hub credentials ----
    public HubCredentialService HubCredentials { get; }
    public FakeClock Clock { get; } = new();

    /// <summary>Controllable time so expiry can be tested without waiting fourteen days.</summary>
    public sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

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

        ReadAuth = new ReadAuthorizer(Db);
        Etags = new ReadEtagProvider(Db);
        EventQueries = new EventQueryService(Db, ReadAuth);
        DivisionQueries = new DivisionQueryService(Db);
        PolicyQueries = new WeighInPolicyQueryService(Db);
        RegistrantQueries = new RegistrantQueryService(Db);
        AccountQueries = new OrganizerAccountQueryService(Db);
        OrganizerRoles = new OrganizerRoleService(Db, Writer, Ids, Authorizer, new NoopEmailSender());
        HubCredentials = new HubCredentialService(Db, Authorizer, Ids, new HubCredentialOptions(), Clock);
    }

    /// <summary>Register an athlete owned by <paramref name="accountId"/> into an open event.</summary>
    public async Task<long> RegisterAsync(long accountId, long eventId, long divisionId, string name = "Athlete",
        double weight = 80, string gender = "M", int age = 25)
    {
        var athleteId = await Registrations.UpsertProfileAsync(accountId, null, new ProfileInput(
            name, new DateOnly(2026, 1, 1).AddYears(-age), 5, weight, "Academy B", gender));
        var result = await Registrations.RegisterAsync(accountId,
            new RegisterInput(eventId, athleteId.Value, [divisionId], PayByCard: false));
        return result.Value.RegistrationId;
    }

    /// <summary>Seed an identity row so account reads can resolve a contact email.</summary>
    public async Task SeedIdentityAsync(long accountId, string email)
    {
        Db.Users.Add(new AppUser
        {
            Id = accountId, AccountId = accountId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(),
        });
        await Db.SaveChangesAsync();
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendConfirmationAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendOrganizerInviteAsync(string toAddress, string token, CancellationToken ct = default) => Task.CompletedTask;
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
