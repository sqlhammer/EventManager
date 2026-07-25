# EventManager — Architecture Overview (orientation draft)

**Status**: Part of the Application Design artifact set (topology + event-flow views). Complements `component-dependency.md` (package graph).
**Source**: tech-env.md "Architecture and Patterns"; requirements.md FR-4 / NFR-6.2.

This is the **hub-and-spoke, offline-first, event-sourced** architecture in two views: (1) topology — who talks to whom; (2) event flow — how data moves without loss.

---

## View 1 — Topology (two worlds: Cloud pre-event, Venue LAN on event day)

```mermaid
flowchart TB
    subgraph CLOUD["☁️ CLOUD (internet, pre-event + mirror)"]
        Backend["cloud-backend<br/>ASP.NET Core Web API<br/>(accounts, registration, RBAC)"]
        PG[("PostgreSQL<br/>event-log mirror")]
        Backend --- PG
    end

    subgraph USERS["👤 Pre-event users (online)"]
        Reg["Registrant app<br/>(athlete / parent)"]
        Coach["Coach app<br/>(bulk register)"]
    end

    subgraph LAN["🏟️ VENUE LAN (event day, internet optional)"]
        Admin["admin-hub = Admin app<br/>MAUI + embedded Kestrel server<br/>+ local SQLite (event log)"]
        Judge["judge-app<br/>MAUI + SQLite queue"]
        Checkin["checkin-app<br/>MAUI + SQLite queue"]
        Admin -->|"SignalR push<br/>(bracket/schedule/results)"| Judge
        Admin -->|"SignalR push"| Checkin
        Judge -->|"scores over WSS<br/>(paired, mat-scoped)"| Admin
        Checkin -->|"check-ins/weigh-ins over WSS<br/>(append-only)"| Admin
    end

    Reg -->|"register (REST/TLS)"| Backend
    Coach -->|"register (REST/TLS)"| Backend
    Backend ==>|"1. download full event<br/>before event day"| Admin
    Admin ==>|"2. replicate event log<br/>async when internet available"| Backend

    style Admin fill:#FFA726,stroke:#E65100,stroke-width:3px,color:#000
    style Backend fill:#42A5F5,stroke:#0D47A1,stroke-width:2px,color:#fff
    style Judge fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Checkin fill:#C8E6C9,stroke:#1B5E20,color:#000
    style PG fill:#90CAF9,stroke:#0D47A1,color:#000
    style Reg fill:#E1BEE7,stroke:#6A1B9A,color:#000
    style Coach fill:#E1BEE7,stroke:#6A1B9A,color:#000
```

**Key point**: the **Admin app IS the hub** — it embeds a web server so Judge/Check-In connect to *it* on the venue LAN, with or without internet. The cloud is only touched twice: download the event beforehand (arrow 1), and mirror the log afterward/whenever online (arrow 2).

### Text alternative
```
CLOUD (online):  Registrant + Coach  --REST/TLS-->  cloud-backend --- PostgreSQL (mirror)

                         | (1) download event before event day
                         v
VENUE LAN (event day, internet optional):
    admin-hub (Admin app = MAUI + embedded Kestrel + SQLite event log)
        <--- scores (WSS, mat-scoped) ---  judge-app  (MAUI + SQLite queue)
        <--- check-ins (WSS, append)  ---  checkin-app (MAUI + SQLite queue)
        ---- SignalR push (brackets/schedule/results) --->  both spokes
                         |
                         | (2) replicate event log async when internet available
                         v
                   cloud-backend  (mirror, never a conflicting source of truth)
```

---

## View 2 — Event flow (why nothing is lost)

Every action becomes an immutable, numbered event. State is a *replay* of the log. Replay is idempotent, so the same event applied twice changes nothing — that is what makes offline queue → reconnect → replay safe.

```mermaid
flowchart LR
    Action["Judge taps<br/>'match scored'"] --> LocalLog["Write event to<br/>local SQLite log<br/>(durable BEFORE ack)"]
    LocalLog --> Ack["UI confirms"]
    LocalLog -.->|"connected"| HubLog["Hub applies event<br/>(AppendIfNotExists)"]
    LocalLog -.->|"offline: queue,<br/>replay on reconnect"| HubLog
    HubLog --> Project["Hub rebuilds projections<br/>(bracket advances)"]
    HubLog -.->|"internet available"| CloudLog["Cloud mirror<br/>(AppendIfNotExists)"]

    style LocalLog fill:#FFE082,stroke:#F57F17,color:#000
    style HubLog fill:#FFA726,stroke:#E65100,color:#000
    style CloudLog fill:#42A5F5,stroke:#0D47A1,color:#fff
```

**The backbone**: `event → local durable write → (queue if offline) → hub applies idempotently → projections update → cloud mirrors idempotently`. The `AppendIfNotExists` idempotence is shown literally in the tech-env.md code sample (lines 86–101).

### Text alternative
```
1. Judge taps "match scored"
2. Event written to device's local SQLite log  (durable BEFORE the UI confirms) -> zero loss
3. If hub reachable: send now.  If offline: queue locally, replay in order on reconnect.
4. Hub applies event with AppendIfNotExists (duplicate replays are no-ops) -> no duplicates
5. Hub rebuilds projections (bracket advances, standings update)
6. When internet is available, hub mirrors the same log to the cloud, also idempotently
```

---

## How this maps to the 5 modules (the D-07 repo layout)

| Module (folder) | Is | Runtime |
|---|---|---|
| `shared/` (**shared-sync-core**, maybe + domain) | Reusable library: event model, log store, idempotent replay, projections (and possibly bracket/scoring engines — that's **Q1/Q2** in the plan) | Consumed by all apps via local NuGet |
| `backend/` (**cloud-backend**) | ASP.NET Core Web API + PostgreSQL, in Docker | Cloud |
| `admin/` (**admin-hub**) | Admin MAUI app + embedded Kestrel = the LAN hub | Organizer's laptop/iPad |
| `judge/` (**judge-app**) | Judge MAUI spoke app | Judges' phones/tablets |
| `checkin/` (**checkin-app**) | Check-In MAUI spoke app | Front-table device |

The design questions (Q1–Q7) are all asking: **which box does each piece of logic live in, and where does it run** — especially how much goes into the shared `shared/` library vs. each app.

---

## As-built: U1 Shared Core (updated 2026-07-24, end-of-unit)

U1 delivered the `shared/` foundation. Internal structure as-built:

```mermaid
flowchart TD
    subgraph SHARED["shared/ (U1 delivered)"]
        Domain["EventManager.Domain<br/>entities + engines<br/>(bracket, seeding, scoring,<br/>weigh-in, RBAC)"]
        Sync["EventManager.Sync<br/>TournamentEvent, IEventStore,<br/>replay, projections,<br/>Snowflake(IdGen), replication"]
    end
    IdGen["IdGen (MIT)"]
    ErrorOr["ErrorOr (MIT)"]
    Domain --> ErrorOr
    Sync --> IdGen
    Sync --> ErrorOr

    style Domain fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Sync fill:#C8E6C9,stroke:#1B5E20,color:#000
    style IdGen fill:#FFE082,stroke:#F57F17,color:#000
    style ErrorOr fill:#FFE082,stroke:#F57F17,color:#000
```

**As-built refinements (vs. the high-level graph in `component-dependency.md`):**
- **`EventManager.Sync` is independent of `EventManager.Domain`.** Event payloads are opaque bytes at the Sync layer, so the event-sourcing plumbing is generic and reusable; concrete domain projections live in consuming units. (The earlier `Sync → Domain` edge is superseded by this cleaner layering.)
- Snowflake ids are generated via **IdGen** behind `IIdGenerator`; expected domain failures use **ErrorOr**.
- Text alt: Domain → ErrorOr; Sync → IdGen, ErrorOr; Domain and Sync are siblings with no edge between them.

## As-built: U2 Contracts & ClientSync (updated 2026-07-25, end-of-unit)

U2 added the wire contracts and the reusable spoke-sync library.

```mermaid
flowchart TD
    Contracts["EventManager.Contracts<br/>DTOs + mapper + validators (FluentValidation)"]
    ClientSync["EventManager.ClientSync<br/>queue, sync client, reconnect,<br/>push consumer, pairing<br/>(ISyncTransport / IHubDiscovery seams)"]
    Sync["EventManager.Sync"]
    Contracts --> Sync
    ClientSync --> Sync
    ClientSync --> Contracts

    style Contracts fill:#C8E6C9,stroke:#1B5E20,color:#000
    style ClientSync fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Sync fill:#A5D6A7,stroke:#1B5E20,color:#000
```

- `Contracts` maps `TournamentEvent` ⇄ `EventEnvelopeDto` (so it references `Sync`, not `Domain`).
- `ClientSync` reuses U1's `IEventStore` (durable queue) and `ProjectionHost` (push apply); the concrete SignalR/WSS transport is injected at app wiring via `ISyncTransport`.
- Text alt: Contracts → Sync; ClientSync → Sync, Contracts.

## As-built: U8 Payment Stub (updated 2026-07-25, end-of-unit)

U8 stood up the **`backend/`** solution (ahead of U3) with the payment-provider seam:
- `backend/EventManager.Payments` — `IPaymentProvider` (D-06 seam) + `StubPaymentProvider` (idempotent, no external call, injectable outcome for decline/timeout/error).
- Self-contained (BCL only); U3's registration flow will consume it and map `PaymentOutcome → PaymentStatus`. A real Stripe adapter replaces the stub post-MVP behind the same interface.
- Text alt: `backend/EventManager.Payments` is a standalone library; no cross-package edges yet (U3 will reference it).

## As-built: U3 Cloud Backend (updated 2026-07-25, end-of-unit)

U3 delivered `backend/EventManager.Api` — the first real API layer (ASP.NET Core Web API + EF Core/PostgreSQL + Docker), consuming U1/U2/U8. Builds green; 20 tests pass (PBT-1..4 + examples).

```mermaid
flowchart TB
    subgraph API["backend/EventManager.Api (U3 delivered)"]
        Ctrls["Controllers<br/>Account · Event · Organizer<br/>Registration · EventIngest · Results"]
        Svcs["Services<br/>Account/Token · Event · Registration<br/>OrganizerRole · Ingest · ResultsQuery"]
        Auth["Auth<br/>JWT + EventAuthorizer<br/>(deny-by-default)"]
        Writer["EventWriter<br/>(validate→append→project)"]
        Proj["CloudProjectionHost<br/>Event/Division/Roster/<br/>Organizer/Results read models"]
        Store["PostgresEventStore : IEventStore<br/>+ Identity + idempotency + refresh tokens"]
        Ctrls --> Svcs
        Svcs --> Auth
        Svcs --> Writer
        Writer --> Proj
        Writer --> Store
        Proj --> Store
    end
    Domain["U1 EventManager.Domain<br/>(RoleAuthorizationPolicy reused)"]
    Sync["U1 EventManager.Sync<br/>(IEventStore, IIdGenerator, serializer)"]
    Contracts["U2 EventManager.Contracts<br/>(EventEnvelope for ingest)"]
    Payments["U8 EventManager.Payments<br/>(IPaymentProvider stub)"]
    PG[("PostgreSQL")]
    Auth --> Domain
    Store --> Sync
    Ctrls --> Contracts
    Svcs --> Payments
    Store --- PG

    style Ctrls fill:#42A5F5,stroke:#0D47A1,color:#fff
    style Svcs fill:#42A5F5,stroke:#0D47A1,color:#fff
    style Auth fill:#42A5F5,stroke:#0D47A1,color:#fff
    style Writer fill:#90CAF9,stroke:#0D47A1,color:#000
    style Proj fill:#90CAF9,stroke:#0D47A1,color:#000
    style Store fill:#90CAF9,stroke:#0D47A1,color:#000
    style Domain fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Sync fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Contracts fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Payments fill:#C8E6C9,stroke:#1B5E20,color:#000
    style PG fill:#90CAF9,stroke:#0D47A1,color:#000
```

**As-built notes:**
- **Two persistence planes (Q1=C)**: ASP.NET Identity tables (accounts/MFA/lockout) + the event-sourced domain plane (events/divisions/registrations/roles) via `PostgresEventStore` — a cloud Npgsql implementation of U1's `IEventStore`. Read models are projections folded synchronously (Q2=A).
- **Authorization does not diverge from the hub**: the cloud reuses U1's exact `RoleAuthorizationPolicy` instance behind `EventAuthorizer` (deny-by-default).
- **Replication ingest** (`EventIngestController`) accepts the hub's replicated log via U2 `EventEnvelope`, event-scoped-authorized (Q7=A) and idempotent; the cloud is a mirror.
- **Deployment**: Docker Compose (Caddy TLS proxy · api · PostgreSQL · backup sidecar); provider-agnostic, no IaC (NFR-6.4).
- Text alt: Controllers → Services → {Auth→U1 Domain policy; EventWriter→CloudProjectionHost→PostgresEventStore→U1 Sync + PostgreSQL}; Controllers→U2 Contracts (ingest); Services→U8 Payments. The topology View 1 `cloud-backend` box is now realized by this internal structure.

## As-built: U4a Hub Core (updated 2026-07-25, end-of-unit, fast-tracked)

U4a delivered `admin/EventManager.Hub` — the LAN hub foundation. **MAUI workload is absent in this environment, so Hub Core ships as an ASP.NET Core library + host; the MAUI Admin UI shell is a deferred seam.** Builds green; 5 tests pass.

```mermaid
flowchart TB
    subgraph HUB["admin/EventManager.Hub (U4a delivered)"]
        HCtrls["Controllers<br/>Pairing · Sync · Device · /health"]
        Pair["PairingService<br/>(one-time token, single-use,<br/>worker-id assign)"]
        DevReg["DeviceRegistry<br/>(list/reassign/revoke)"]
        OAuth["OfflineOrganizerAuth<br/>(hub-side RBAC)"]
        Intake["SyncIntakeService<br/>(idempotent spoke intake)"]
        HProj["HubProjectionHost<br/>(DeviceRecord read model)"]
        HStore["HubEventStore : IEventStore<br/>(SQLite; SQLCipher deferred)"]
        Seams["Seams: IHubPush · IMdnsAdvertiser<br/>(SignalR/mDNS deferred)"]
        HCtrls --> Pair & DevReg & Intake
        Pair --> HStore
        DevReg --> HStore
        Intake --> HStore
        HProj --> HStore
    end
    Domain["U1 Domain<br/>(RoleAuthorizationPolicy,<br/>WorkerIdRegistry)"]
    Sync["U1 Sync (IEventStore, IIdGenerator)"]
    Contracts["U2 Contracts<br/>(pairing/push DTOs, EventEnvelope)"]
    OAuth --> Domain
    Pair --> Domain
    HStore --> Sync
    HCtrls --> Contracts

    style HCtrls fill:#FFA726,stroke:#E65100,color:#000
    style Pair fill:#FFCC80,stroke:#E65100,color:#000
    style DevReg fill:#FFCC80,stroke:#E65100,color:#000
    style OAuth fill:#FFCC80,stroke:#E65100,color:#000
    style Intake fill:#FFCC80,stroke:#E65100,color:#000
    style HProj fill:#FFCC80,stroke:#E65100,color:#000
    style HStore fill:#FFCC80,stroke:#E65100,color:#000
    style Seams fill:#FFE082,stroke:#F57F17,color:#000
    style Domain fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Sync fill:#C8E6C9,stroke:#1B5E20,color:#000
    style Contracts fill:#C8E6C9,stroke:#1B5E20,color:#000
```

**As-built notes:**
- **Hub event store** = `HubEventStore : IEventStore` (SQLite) — the local counterpart to the cloud's `PostgresEventStore`; same idempotent-append contract. SQLCipher at-rest (D-09) is a deferred seam.
- **Pairing** issues single-use tokens and assigns Snowflake worker ids via U1 `WorkerIdRegistry`; **device revocation** frees the worker id and rejects the credential on next spoke contact (US-508).
- **Hub-side RBAC reuses U1's `RoleAuthorizationPolicy`** (identical to cloud, D-27) over credentials packaged at event download.
- **Deferred seams**: MAUI Admin UI shell; concrete SignalR/WSS push (`IHubPush`); concrete mDNS (`IMdnsAdvertiser`); SQLCipher; hub→cloud replication client (U7 owns S-7 client side).
- Text alt: Controllers → {PairingService, DeviceRegistry, SyncIntakeService} → HubEventStore→U1 Sync; OfflineOrganizerAuth+PairingService→U1 Domain (policy, worker-id); Controllers→U2 Contracts. Realizes the `admin-hub` box's server side in topology View 1; U4b adds bracket/scoring/competition on top.
