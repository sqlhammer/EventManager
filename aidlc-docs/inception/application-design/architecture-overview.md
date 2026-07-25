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
