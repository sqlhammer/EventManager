using EventManager.Contracts;
using EventManager.Domain.Engines;
using EventManager.Hub.Competition;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Hub.Projections;
using EventManager.Hub.Services;
using EventManager.Sync;
using Microsoft.EntityFrameworkCore;

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

// Health: connected-device status (US-302, NFR-3.7).
app.MapGet("/health", async (HubDbContext db) =>
{
    var active = await db.Devices.CountAsync(d => !d.Revoked);
    return Results.Ok(new { status = "Healthy", connectedDevices = active });
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
