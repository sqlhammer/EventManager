# EventManager — Technical Environment

## Languages
| Category   | Language & version |
| ---------- | ------------------ |
| Required   | C# 13 / .NET 10 (LTS, all client apps and backend) |
| Permitted  | SQL (T-SQL/PostgreSQL dialect) for queries, views, and migrations |
| Prohibited | JavaScript/TypeScript, Swift, Kotlin, Python, Go, Java — no separate native or web-stack client codebases; .NET MAUI covers all client platforms |

## Frameworks and Libraries
| Category   | Item | Rationale / alternative |
| ---------- | ---- | ----------------------- |
| Required   | .NET MAUI | Single codebase across Admin (Windows/Mac/iPad) and Judge/Check-In/Spectator (iOS/Android) apps; shared C# sync and event-log logic across every client |
| Required   | ASP.NET Core Web API | Cloud backend REST API; same language/runtime as clients |
| Required   | Entity Framework Core | ORM for both cloud (Npgsql provider on PostgreSQL) and local (SQLite provider) persistence; one data-access pattern across the stack |
| Required   | Microsoft.Data.Sqlite / EF Core SQLite provider | On-device local database for all four client apps |
| Required   | xUnit | Test framework for backend and client logic |
| Required   | ASP.NET Core Identity + JWT | Self-hosted auth for organizer/admin accounts; no third-party IdP dependency |
| Preferred  | SignalR (over WebSocket) | Real-time push from the Admin hub's embedded Kestrel server to Judge/Check-In/Spectator apps on the LAN; native .NET fit for the hub-and-spoke sync model |
| Preferred  | A .NET mDNS/Zeroconf library (e.g. Makaretu.Dns) | LAN auto-discovery of the Admin hub; falls back to manual IP entry / QR code pairing if mDNS is blocked |
| Prohibited | React Native, Electron, Flutter | Ruled out in favor of a single MAUI codebase across all client apps |
| Prohibited | Progressive Web App / Service Workers | No PWA fallback for spectators — native MAUI app only, per single-stack decision |

## Data & Persistence
Local storage: SQLite on every client app (Admin, Judge, Check-In, Spectator), accessed through EF Core's SQLite provider. Each app maintains its own local event-log table plus whatever read-model tables it needs (bracket state, roster, schedule).

Cloud storage: PostgreSQL, accessed through EF Core's Npgsql provider. The cloud database mirrors the Admin hub's event log — it is a replica, not a second source of truth.

Data pattern: event-sourcing. Every state change (score entry, check-in, weigh-in, bracket advancement, schedule update) is written as an immutable, timestamped, sequence-numbered event. Current state is a projection built by replaying the event log. This makes offline queuing, LAN sync, and cloud reconciliation deterministic and idempotent.

Migrations: EF Core Migrations on both sides — Npgsql migrations for the cloud schema, SQLite migrations for the local client schema. Local schema migrations must never run automatically during an active tournament; they run on app upgrade only, with a rollback path.

## Architecture and Patterns
API style: REST for all cloud backend endpoints.

Sync topology: hub-and-spoke. The Admin app embeds a local ASP.NET Core (Kestrel) server and acts as the LAN hub. Judge, Check-In, and Spectator apps discover the hub via mDNS (manual IP/QR fallback if blocked) and sync over SignalR/WebSocket. The Admin hub is authoritative for bracket structure, divisions, and schedule; each Judge app is authoritative only for its own assigned mat; Check-In is append-only; Spectator apps are read-only.

Cloud sync: the Admin hub replicates its event log to the cloud backend asynchronously whenever internet is available. On reconnect after an outage, events generated offline replay to the cloud in sequence order. The cloud backend never conflicts with the hub — it is a mirror.

Deployment model: cloud backend runs in Docker containers (VPS/ECS/ACI). Client apps are installed natively — Admin via desktop install (Windows/Mac) or iPad app install, Judge/Check-In/Spectator via mobile app store installs.

Project structure: separate repository per app (Admin, Judge, Check-In, Spectator, cloud backend), with shared sync/event-log logic published as a versioned .NET package consumed by all of them.

## Security
Auth: ASP.NET Core Identity backed by the PostgreSQL database, issuing JWT bearer tokens for organizer/admin accounts on the cloud backend. No third-party identity provider.

LAN trust model: devices on the same local network as the Admin hub are implicitly trusted for sync (matches the tournament domain's natural mat-ownership boundaries); this should be revisited if EventManager ever needs to operate on untrusted/shared venue networks.

Encryption: TLS for all cloud API traffic. LAN WebSocket traffic and at-rest encryption for the local SQLite database are open implementation questions — see Risks and Open Questions in the vision document.

Input validation: Data Annotations / FluentValidation on all API request models before they reach the event-log write path.

Secrets management: cloud provider secrets manager or environment variables injected at deploy time — no self-hosted Vault.

## Testing
Test framework: xUnit for both backend and client (MAUI) logic.

Coverage target: 80%+ on core sync and event-log logic (the event-sourcing engine, conflict resolution, replay); lighter coverage elsewhere (UI, simple CRUD).

CI/CD gates: build, unit tests, and coverage threshold must all pass before merge. Integration tests covering actual hub/sync scenarios (LAN disconnect, reconnect replay, hub failover) are recommended for later phases but are not yet a required CI gate.

## Example Code

One endpoint — cloud backend receiving a batch of events replayed from the Admin hub after reconnect:

    [ApiController]
    [Route("api/events")]
    public class EventIngestController : ControllerBase
    {
        private readonly IEventLogStore _store;

        public EventIngestController(IEventLogStore store) => _store = store;

        [HttpPost("batch")]
        [Authorize]
        public async Task<IActionResult> IngestBatch([FromBody] IReadOnlyList<TournamentEvent> events)
        {
            foreach (var evt in events.OrderBy(e => e.SequenceNumber))
            {
                await _store.AppendIfNotExistsAsync(evt);
            }
            return Ok(new { accepted = events.Count });
        }
    }

One function — idempotent event apply, the core of the replay/conflict-resolution logic:

    public async Task AppendIfNotExistsAsync(TournamentEvent evt)
    {
        var exists = await _db.Events
            .AnyAsync(e => e.DeviceId == evt.DeviceId && e.SequenceNumber == evt.SequenceNumber);

        if (exists)
        {
            return; // already applied — safe to replay
        }

        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        await _projector.ApplyAsync(evt); // update current-state read models
    }

One test — verifying replay is idempotent:

    public class EventLogStoreTests
    {
        [Fact]
        public async Task AppendIfNotExistsAsync_ReplayedEvent_IsNotDuplicated()
        {
            var store = TestEventLogStore.Create();
            var evt = new TournamentEvent(DeviceId: "judge-mat-3", SequenceNumber: 42, Type: "MatchScored");

            await store.AppendIfNotExistsAsync(evt);
            await store.AppendIfNotExistsAsync(evt); // simulate replay after reconnect

            var count = await store.CountEventsAsync(evt.DeviceId, evt.SequenceNumber);
            Assert.Equal(1, count);
        }
    }
