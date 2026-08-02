using EventManager.Contracts;
using EventManager.Domain.Engines;
using EventManager.Hub.Competition;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Hub.Projections;
using EventManager.Hub.Resilience;
using EventManager.Hub.Services;
using EventManager.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Hub-local SQLite store (SQLCipher at-rest is a deferred seam, D-09).
var dbPath = builder.Configuration.GetConnectionString("HubDb") ?? "Data Source=hub.db";
builder.Services.AddDbContext<HubDbContext>(o => o.UseSqlite(dbPath));

// Shared singletons (U1/U2)
builder.Services.AddSingleton<IEventSerializer>(new JsonEventSerializer());
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId: 1)); // hub worker range
builder.Services.AddSingleton<IRoleAuthorizationPolicy, RoleAuthorizationPolicy>();
builder.Services.AddSingleton<IWorkerIdRegistry>(new WorkerIdRegistry());
builder.Services.AddSingleton(new HubIdentity());
builder.Services.AddSingleton<IHubPush, InProcessHubPush>();
builder.Services.AddSingleton<IMdnsAdvertiser, NoopMdnsAdvertiser>();

// U1 competition engines (pure, stateless)
builder.Services.AddSingleton<ISeedingEngine, SeedingEngine>();
builder.Services.AddSingleton<IBracketEngine, BracketEngine>();
builder.Services.AddSingleton<IScoringEngine, ScoringEngine>();
builder.Services.AddSingleton<IWeighInPolicyEvaluator, WeighInPolicyEvaluator>();

// U7 offline-resilience (replication protocol + backup/recovery).
builder.Services.AddSingleton<IReplicationProtocol, ReplicationProtocol>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<RecoveryService>();

// ---- U10 hub→cloud HTTP replication (the seam U7 deferred) ----
builder.Services.AddOptions<ReplicationOptions>()
    .Bind(builder.Configuration.GetSection(ReplicationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();                       // a bad value fails at startup, not at 2am mid-event
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ReplicationOptions>>().Value);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ReplicationSignal>();
builder.Services.AddSingleton<ReplicationStatus>();
builder.Services.AddSingleton<ReplicationMetrics>();
builder.Services.AddSingleton<ReplicationCircuitBreaker>();
builder.Services.AddScoped<HubCredentialStore>();
builder.Services.AddHttpClient(HttpCloudReplicationTransport.HttpClientName);

// DPAPI is Windows-only at runtime. Selecting the pass-through elsewhere would reopen the
// SECURITY-12 finding that F1=B closed, so it is logged loudly rather than chosen quietly.
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
}
else
{
    builder.Services.AddSingleton<ISecretProtector, PassthroughSecretProtector>();
}

builder.Services.AddSingleton<ICloudReplicationTransport>(sp => new HttpCloudReplicationTransport(
    sp.GetRequiredService<IHttpClientFactory>(),
    // The transport is a singleton but the credential store is scoped (it holds the DbContext), so
    // it is resolved per use rather than captured — the same captive-dependency hazard CL-1=A
    // addresses for the client.
    new ScopedHubCredentialReader(sp.GetRequiredService<IServiceScopeFactory>()),
    sp.GetRequiredService<ReplicationCircuitBreaker>(),
    sp.GetRequiredService<ReplicationStatus>(),
    sp.GetRequiredService<ReplicationMetrics>(),
    sp.GetRequiredService<ReplicationOptions>(),
    sp.GetRequiredService<ILogger<HttpCloudReplicationTransport>>()));

builder.Services.AddSingleton<ReplicationClient>(sp => new ReplicationClient(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IReplicationProtocol>(),
    sp.GetRequiredService<ICloudReplicationTransport>(),
    sp.GetRequiredService<ReplicationSignal>(),
    sp.GetRequiredService<ReplicationStatus>(),
    sp.GetRequiredService<ReplicationOptions>(),
    sp.GetRequiredService<ILogger<ReplicationClient>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReplicationClient>());

// Metrics export (TS-U10-1). The collector is in the cloud, so it is unreachable during exactly the
// outages this unit exists to survive — /health is the venue-visible signal, not this (U10-CON-2).
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddMeter(ReplicationMetrics.MeterName)
    .AddOtlpExporter());

// Hub-core scoped components
builder.Services.AddScoped<IEventStore, HubEventStore>();
builder.Services.AddScoped<HubProjectionHost>();
builder.Services.AddScoped<HubEventWriter>();
builder.Services.AddScoped<PairingService>();
builder.Services.AddScoped<DeviceRegistry>();
builder.Services.AddScoped<OfflineOrganizerAuth>();
builder.Services.AddScoped<SyncIntakeService>();
builder.Services.AddScoped<EventDownloadService>();

// U4b competition services
builder.Services.AddScoped<BracketService>();
builder.Services.AddScoped<ScoringIntakeService>();
builder.Services.AddScoped<WeighInResolutionService>();
builder.Services.AddScoped<DivisionFinalizationService>();
builder.Services.AddScoped<DisputeService>();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

// Health: connected-device status (US-302, NFR-3.7) + replication status (US-806, U10-FR-17).
// Replication figures are served from the CACHED snapshot, not recomputed: this endpoint is probed
// frequently, and a store query per probe on a machine running a live event is not worth the
// accuracy. The human-facing GET /api/replication/status computes them live instead (ND-Q6=C).
app.MapGet("/health", async (HubDbContext db, ReplicationStatus replication) =>
{
    var active = await db.Devices.CountAsync(d => !d.Revoked);
    return Results.Ok(new { status = "Healthy", connectedDevices = active, replication = replication.Snapshot() });
});

// Start-up: ensure the local store exists and advertise the hub on the LAN (mDNS seam).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    await db.Database.EnsureCreatedAsync();
    var hub = app.Services.GetRequiredService<HubIdentity>();
    app.Services.GetRequiredService<IMdnsAdvertiser>()
        .Advertise(new HubDiscoveryInfoDto(hub.HubAddress, hub.Port, hub.CertFingerprint));
}

app.Run();

/// <summary>Assembly marker for tests.</summary>
public partial class Program;
