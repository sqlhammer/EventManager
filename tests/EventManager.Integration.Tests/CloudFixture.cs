using EventManager.Api.Persistence;
using EventManager.Api.Services;
using EventManager.Hub.Resilience;
using EventManager.Sync;
using EventManager.Api.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventManager.Integration.Tests;

/// <summary>
/// Runs the real cloud API in-process over SQLite, and wires the real hub transport to it.
///
/// Two substitutions, both deliberate and both narrow:
/// <list type="bullet">
/// <item>SQLite instead of PostgreSQL — the same substitution every other service test in this repo
/// makes, because the persistence layer is provider-agnostic.</item>
/// <item>A controllable clock, so credential expiry can be tested without waiting fourteen days.</item>
/// </list>
/// The credential itself, the authentication handler, the authorization check, the ingest path, and
/// the hub transport are all the production implementations.
///
/// Note the entry-point marker: both <c>EventManager.Api</c> and <c>EventManager.Hub</c> declare a
/// global <c>Program</c> class, so referencing both makes that name ambiguous. This is the first
/// place in the repo where the two are referenced together (U10-CON-4), so it is the first place the
/// collision can occur — hence a controller type as the assembly marker instead.
/// </summary>
public sealed class CloudFixture : WebApplicationFactory<EventIngestController>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public FakeClock Clock { get; } = new();

    public sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // The API refuses to start outside Development without a signing key — a deliberate guard
        // (Program.cs:28). Supply a test key rather than switching to Development, which would also
        // turn on startup auto-migration and fight EnsureCreated below.
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-used-by-hub-credentials");
        builder.ConfigureServices(services =>
        {
            // AddDbContext also registers an options-configuration service; removing only
            // DbContextOptions leaves the Npgsql configuration in place and EF then sees two.
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            // The app registers the Npgsql provider, and EF refuses to see two providers in one
            // container. Giving the SQLite context its own internal provider isolates them instead
            // of trying to unpick Npgsql's registrations, which would be fragile.
            var sqliteProvider = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();
            services.AddDbContext<AppDbContext>(o => o
                .UseSqlite(_connection)
                .UseInternalServiceProvider(sqliteProvider));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        AccountId = scope.ServiceProvider.GetRequiredService<IIdGenerator>().NextId();
    }

    public long AccountId { get; private set; }

    /// <summary>
    /// Creates a fresh event and issues a credential for it through the real service, exactly as the
    /// controller would. A fresh event per test rather than a shared one, because the cap of three
    /// active credentials per event (BR-REPL-5) is real behaviour and would otherwise make tests
    /// depend on each other's ordering.
    /// </summary>
    public async Task<(long EventId, string Key)> IssueCredentialAsync(string label)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var credentials = scope.ServiceProvider.GetRequiredService<HubCredentialService>();

        var eventId = await SeedEventAsync(db, ids);
        var issued = await credentials.IssueAsync(AccountId, eventId, label);
        Assert.False(issued.IsError);
        _lastEventId = eventId;
        return (eventId, issued.Value.Key);
    }

    /// <summary>Revokes every credential on the most recently issued event.</summary>
    public async Task RevokeAllAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<HubCredentialService>();
        var ids = await db.HubCredentials.Where(c => c.EventScopeId == _lastEventId)
            .Select(c => c.CredentialId).ToListAsync();
        foreach (var id in ids) await credentials.RevokeAsync(AccountId, _lastEventId, id);
    }

    private long _lastEventId;

    private async Task<long> SeedEventAsync(AppDbContext db, IIdGenerator ids)
    {
        var eventId = ids.NextId();
        db.EventRows.Add(new EventRow
        {
            EventId = eventId, Name = "Integration Open", Venue = "Dojo",
            Date = new DateOnly(2026, 9, 1), RegistrationStart = new DateOnly(2026, 8, 1),
            RegistrationEnd = new DateOnly(2026, 8, 20), EntryFee = 50m, CreatedByAccountId = AccountId,
        });
        db.OrganizerRows.Add(new OrganizerRow
        {
            Id = ids.NextId(), EventId = eventId, AccountId = AccountId,
            Role = nameof(EventManager.Domain.OrganizerRole.FullAdmin),
        });
        await db.SaveChangesAsync();
        return eventId;
    }

    /// <summary>
    /// The real hub transport, pointed at the in-process cloud. The insecure-URL override is enabled
    /// because <see cref="WebApplicationFactory{T}"/> serves over http://localhost — the HTTPS rule
    /// itself is verified separately in the hub's own tests.
    /// </summary>
    public HttpCloudReplicationTransport TransportFor(string key)
    {
        var options = new ReplicationOptions { AllowInsecureBaseUrl = true };
        var status = new ReplicationStatus();
        var metrics = new ReplicationMetrics(status);
        var breaker = new ReplicationCircuitBreaker(options, TimeProvider.System);
        var reader = new FixedCredentialReader(key, Server.BaseAddress!.ToString());

        return new HttpCloudReplicationTransport(
            new FixtureHttpClientFactory(CreateClient()), reader, breaker, status, metrics, options,
            NullLogger<HttpCloudReplicationTransport>.Instance);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
        Dispose();
    }

    private sealed class FixedCredentialReader(string key, string baseUrl) : IHubCredentialReader
    {
        public Task<HubCloudCredential?> TryGetAsync(CancellationToken ct = default) =>
            Task.FromResult<HubCloudCredential?>(new HubCloudCredential(key, baseUrl, DateTimeOffset.UtcNow));
    }

    /// <summary>Hands the transport the factory's client so requests reach the in-process server.</summary>
    private sealed class FixtureHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
