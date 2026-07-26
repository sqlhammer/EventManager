# EventManager — Technical Environment

## Languages
| Category   | Language & version |
| ---------- | ------------------ |
| Required   | C# 13 / .NET 10 (LTS) — backend, native MAUI operational apps, and the Blazor web portal |
| Permitted  | SQL (T-SQL/PostgreSQL dialect) for queries, views, and migrations |
| Permitted  | HTML/CSS + Razor markup for the Blazor web portal; small, targeted JS interop only where Blazor requires it (no application logic in JS) |
| Prohibited | JavaScript/TypeScript **as an application language**, Swift, Kotlin, Python, Go, Java — the web UI is Blazor (C#), so no JS/TS SPA framework or separate web-stack codebase is introduced; C# spans backend + all UIs |

## Frameworks and Libraries
| Category   | Item | Rationale / alternative |
| ---------- | ---- | ----------------------- |
| Required   | .NET MAUI | **Venue-day operational apps** (offline/LAN): Judge, Check-In, on-venue Spectator, and the Admin **hub host** (embeds the Kestrel LAN server). Native is required here for offline-first, LAN sync, and device pairing — a browser cannot fill this role. Shared C# sync/event-log logic across every native client. |
| Required   | Blazor (Blazor Web App — server-rendered with interactive components, .NET 10) | **Public web portal** (online, zero-install): registrant/coach self-signup, athlete profiles, event registration (incl. coach bulk), payment, results viewing — **and organizer event management** (create/configure events, divisions, registration windows, RBAC, roster/payment). Runs inside the cloud backend, reusing `EventManager.Contracts` DTOs + validators. Keeps all UI in C#. |
| Required   | ASP.NET Core Web API | Cloud backend REST API; same language/runtime as every client. Serves both the native apps and the Blazor portal. |
| Required   | Entity Framework Core | ORM for both cloud (Npgsql provider on PostgreSQL) and local (SQLite provider) persistence; one data-access pattern across the stack |
| Required   | Microsoft.Data.Sqlite / EF Core SQLite provider | On-device local database for the native operational apps |
| Required   | xUnit | Test framework for backend, native client logic, and the web portal |
| Required   | ASP.NET Core Identity | Self-hosted account store (register/login/MFA/lockout), no third-party IdP. Fronted by two token strategies below. |
| Required   | JWT bearer (native apps) + cookie / backend-for-frontend session (Blazor portal) | Native apps hold bearer JWTs; the browser portal uses a server-side cookie/BFF session so **access tokens never live in browser JavaScript** (XSS protection). One Identity store, two front-door strategies. |
| Preferred  | SignalR (over WebSocket) | Real-time push from the Admin hub's embedded Kestrel server to Judge/Check-In/Spectator apps on the LAN; native .NET fit for the hub-and-spoke sync model |
| Preferred  | A .NET mDNS/Zeroconf library (e.g. Makaretu.Dns) | LAN auto-discovery of the Admin hub; falls back to manual IP entry / QR code pairing if mDNS is blocked |
| Prohibited | React Native, Electron, Flutter, and JS/TS SPA frameworks (React, Angular, Vue) | The single-language decision stands: native clients are MAUI, the web UI is Blazor — no JavaScript/TypeScript application framework enters the stack |
| Out of scope | Offline / PWA mode for the web portal | The offline-first story belongs to the native operational apps; the Blazor portal is an online-only surface (it talks to the cloud, which is internet-facing by definition) |

## Data & Persistence
Local storage: SQLite on every **native operational app** (Admin hub, Judge, Check-In, Spectator), accessed through EF Core's SQLite provider. Each app maintains its own local event-log table plus whatever read-model tables it needs (bracket state, roster, schedule). The Blazor web portal has **no local database** — it is server-side and reads/writes the cloud PostgreSQL through the API/services.

Cloud storage: PostgreSQL, accessed through EF Core's Npgsql provider. The cloud database mirrors the Admin hub's event log — it is a replica, not a second source of truth.

Data pattern: event-sourcing. Every state change (score entry, check-in, weigh-in, bracket advancement, schedule update) is written as an immutable, timestamped, sequence-numbered event. Current state is a projection built by replaying the event log. This makes offline queuing, LAN sync, and cloud reconciliation deterministic and idempotent.

Migrations: EF Core Migrations on both sides — Npgsql migrations for the cloud schema, SQLite migrations for the local client schema. Local schema migrations must never run automatically during an active tournament; they run on app upgrade only, with a rollback path.

## Architecture and Patterns
API style: REST for all cloud backend endpoints.

Presentation surfaces (two planes, one backend): The system has two distinct UI contexts, and each uses the right tool.
- **Public web portal (Blazor, online):** the internet-facing self-service front door — registrant/coach signup, athlete profiles, event registration + coach bulk, payment, results viewing — **plus organizer event management** (event/division setup, registration windows, RBAC, roster/payment). Zero-install; reached from any browser. Served **same-origin as the API behind the TLS proxy** (`/` → portal, `/api` → REST API), so no CORS and no cross-origin token handling. To support self-registration, the API adds **anonymous public read endpoints** (browse open events + view event/divisions) — the only unauthenticated read surface; everything else stays authorized.
- **Native MAUI operational apps (offline/LAN):** the venue-day tools — Judge, Check-In, on-venue Spectator, and the **Admin hub host** (which embeds the Kestrel LAN server). These stay native for offline-first operation, LAN sync, and device pairing. The Admin native app's job is now **only the venue-day hub**; all pre-event organizer setup moves to the web portal, so that admin UI is built once (web), not twice.

Sync topology: hub-and-spoke. The Admin app embeds a local ASP.NET Core (Kestrel) server and acts as the LAN hub. Judge, Check-In, and Spectator apps discover the hub via mDNS (manual IP/QR fallback if blocked) and sync over SignalR/WebSocket. The Admin hub is authoritative for bracket structure, divisions, and schedule; each Judge app is authoritative only for its own assigned mat; Check-In is append-only; Spectator apps are read-only.

Cloud sync: the Admin hub replicates its event log to the cloud backend asynchronously whenever internet is available. On reconnect after an outage, events generated offline replay to the cloud in sequence order. The cloud backend never conflicts with the hub — it is a mirror.

Deployment model: cloud backend **and the Blazor web portal** run in Docker containers (VPS/ECS/ACI) behind the existing TLS reverse proxy — the portal is part of the cloud deployment, not a separate host. The native operational apps are installed on-device — the Admin **hub host** via desktop install (Windows/Mac) or iPad app install, Judge/Check-In/Spectator via mobile app store installs.

Project structure: separate repository per native app (Admin hub, Judge, Check-In, Spectator) plus the cloud backend, with shared sync/event-log logic published as a versioned .NET package consumed by all of them. The **Blazor web portal (`EventManager.Web`) lives in the backend solution** and shares `EventManager.Contracts` + `EventManager.Domain` with the API rather than duplicating models.

## Security
Auth: one ASP.NET Core Identity store (backed by PostgreSQL), fronted by two token strategies for two front doors. **Native apps** receive **JWT bearer** tokens. **The Blazor web portal** authenticates browsers with a **server-side cookie / backend-for-frontend session** — the JWT is held and refreshed on the server, never exposed to browser JavaScript (defends against XSS token theft) — and carries anti-forgery (CSRF) protection on state-changing requests, which the bearer API does not need. No third-party identity provider.

Public web surface: only the **anonymous read endpoints** (browse open events, view event/divisions) are unauthenticated, so registrants can discover events before signing up. Every write and every account-scoped read stays authorized. The portal is internet-facing, so signup keeps the existing non-enumerating responses, rate limiting, and progressive lockout, and should add bot mitigation (e.g. CAPTCHA) on the public register form.

LAN trust model: devices on the same local network as the Admin hub are implicitly trusted for sync (matches the tournament domain's natural mat-ownership boundaries); this should be revisited if EventManager ever needs to operate on untrusted/shared venue networks.

Encryption: TLS for all cloud API traffic. LAN WebSocket traffic and at-rest encryption for the local SQLite database are open implementation questions — see Risks and Open Questions in the vision document.

Input validation: Data Annotations / FluentValidation on all API request models before they reach the event-log write path.

Secrets management: cloud provider secrets manager or environment variables injected at deploy time — no self-hosted Vault.

## Testing
Test framework: xUnit for backend, native (MAUI) client, and web-portal logic; bUnit (xUnit-based) is the preferred choice for Blazor component tests.

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
