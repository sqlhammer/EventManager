# Application Design Plan — EventManager

**Stage**: INCEPTION - Application Design
**Date**: 2026-07-24
**Scope**: High-level component identification, interfaces, service layer, and dependencies across the 5 modules (shared, backend, admin/hub, judge, checkin). Detailed business logic is deferred to per-unit Functional Design (CONSTRUCTION).

---

## Part 1 — Design Questions (please answer before generation)

These questions resolve design decisions that materially change the component boundaries. Answer each by filling the `[Answer]:` tag. Choose the last option (Other) if none fit, and describe.

### Question 1 — Domain logic placement (bracket & scoring engines)
The bracket-generation/seeding engine and the scoring engines (point-sparring, forms/kata) are pure, correctness-critical domain logic and the heaviest PBT targets. Where should they live?

A) In the **shared-sync-core** library (or a sibling shared domain library), so the hub owns authoritative execution but the same tested code can be reused by any client for preview/validation

B) In the **admin-hub** module only (the hub is the sole authority for brackets/advancement anyway), keeping the shared library focused purely on event-log/sync plumbing

C) Split — a shared **domain** library separate from the shared **sync** library (two shared packages: `EventManager.Domain` + `EventManager.Sync`), so domain rules and sync plumbing version independently

D) Other (please describe after [Answer]: tag below)

[Answer]: C

### Question 2 — Event store abstraction
Both the hub (SQLite) and the cloud backend (PostgreSQL) persist the append-only event log. How should the event-store interface be structured?

A) One shared `IEventStore` abstraction in the sync library with two implementations (SQLite, PostgreSQL) — maximizes shared replay/idempotence logic

B) Shared **interface + shared replay/projection logic**, but each side has its own thin persistence adapter — shared core owns correctness, adapters own storage specifics

C) Separate implementations per side (no shared store abstraction) — accept some duplication for independence

D) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 3 — Projection strategy
Current state (roster, divisions, brackets, standings) is a projection of the event log. How are projections maintained?

A) **Rebuilt in-memory on startup** by folding the log, then updated incrementally as new events arrive (simplest; cold-start replay must meet NFR-5.3 < 30s)

B) **Persisted projections** (materialized tables) updated incrementally, with the log as the rebuild source of truth (faster cold start, more moving parts)

C) Hybrid — in-memory on spokes (small), persisted on hub/cloud (larger)

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 4 — Spoke client architecture / shared client sync
The Judge and Check-In apps share the same offline behavior (local SQLite queue, durable-before-ack, idempotent replay, auto-reconnect, SignalR consumption). How should this be organized?

A) A shared **client-sync library** (consumed by both spoke apps via the local NuGet feed) encapsulating queue/replay/reconnect; apps add only their UI + role-specific flows

B) Each app implements its own sync client (more duplication, fully independent)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 5 — RBAC authorization enforcement location
Organizer RBAC (Full Admin / Co-Organizer, FR-1.6/2.7/2.8) governs both cloud actions (pre-event) and hub actions (event-day, offline).

A) **Both** — cloud enforces for cloud endpoints; the hub independently enforces organizer role for event-day admin actions using role assignments downloaded with the event (works fully offline)

B) **Cloud-only** — organizer role checks happen in the cloud; on the hub, physical possession of the authenticated Admin device is treated as sufficient authority (simpler, but no per-role distinction offline)

C) Other (please describe after [Answer]: tag below)

[Answer]: Talk me through this in more detail for us to decide. → Refined into Q5a + Q5b below (2026-07-24).

#### Question 5a — Event-day organizer topology
How many Admin devices are live on event day, and can co-organizers act from their own devices?

A) **Single Admin/hub device** — organizers share one Admin device on event day; a co-organizer who needs to act does so on that device, authenticated as themselves. No new "admin client" capability; matches the "1–3 organizers, co-located" scale note (recommended for MVP)

B) **Multiple admin devices** — one Admin app is the hub; additional organizers connect from their own Admin app instances as authenticated admin clients and can act concurrently (new capability — adds an admin-client to the design and enlarges scope)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

#### Question 5b — Hub RBAC enforcement
Does the hub enforce organizer role (Full Admin vs Co-Organizer) on event-day admin actions?

A) **Hub enforces role** — using role assignments packaged at event download and offline authentication of the acting organizer; server-side role checks on the hub, Security-Baseline (SECURITY-08) compliant, works offline. Mandatory if 5a=B; recommended even if 5a=A so Full-Admin-only actions (delete event, remove/demote organizer, transfer Full Admin) stay gated on event day (recommended)

B) **Cloud-only role checks** — on the hub, the authenticated Admin device is treated as sufficient authority for all organizer actions; **consciously accept a SECURITY-08 deviation on the hub** (simpler, but the Full-Admin/Co-Organizer distinction does not hold offline)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

> Note: the offline **authentication mechanism** for Q5b=A (cached JWT validated via the cloud's public key vs. a per-organizer verifier downloaded with the event vs. an admin-pairing flow) is deferred to Functional Design; only the enforce/don't-enforce decision is needed here. Reality either way: offline, the hub can only authenticate organizers whose credentials/tokens were cached at or before event download.

### Question 6 — Shared API/DTO contracts
Cloud API and hub LAN API request/response contracts (DTOs) — how are they shared to keep clients in sync?

A) A shared **contracts** package (DTOs + validation attributes) in the shared library, consumed by server and clients via local NuGet — single source of truth

B) Per-app DTOs (each client defines its own), contracts documented but not code-shared

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 7 — Client presentation pattern
For the .NET MAUI client apps (Admin, Judge, Check-In):

A) **MVVM** (the MAUI-idiomatic default) with data binding and a shared view-model base where useful

B) MVU / other reactive pattern

C) No preference — you choose the idiomatic default (MVVM)

D) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Identifier Strategy — Snowflake IDs (user direction, 2026-07-24)

**User direction**: Use **Snowflake IDs** (https://en.wikipedia.org/wiki/Snowflake_ID) for identifiers accessed between apps, so every app can mint IDs locally that are globally unique and time-sortable — e.g., the shared-sync-core event log's `EventId`.

**Confirmed design intent** (applies unless a question below overrides): a Snowflake **generator lives in shared-sync-core**; `TournamentEvent` gets a Snowflake `EventId` (64-bit, time-ordered) used as its primary key, idempotence key for `AppendIfNotExists`, and cross-app reference. Standard bit layout unless specified otherwise (custom epoch + ~41-bit ms timestamp / ~10-bit worker / ~12-bit sequence). Stored as signed 64-bit `BIGINT` (PostgreSQL) / `INTEGER` (SQLite); all-.NET stack so no JS number-precision concern. Intra-generator monotonicity is guaranteed; cross-device time ordering is best-effort, bounded by device clock accuracy — the authoritative per-stream order remains the per-device sequence (see Q9).

### Question 8 — Snowflake scope
Which identifiers become Snowflake IDs?

A) Event log `EventId` only

B) All identifiers created on one node and referenced across apps or in the shared log — event, division, registration, match, device IDs, etc. — while local-only read-model surrogate keys stay as they are (recommended, matches "IDs accessed between apps")

C) All primary keys everywhere, including purely local read-model rows

D) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 9 — Per-device sequence number: keep alongside, or replace?
US-504/FR-4.6 need a gap-free contiguous per-device sequence for replication completeness ("no gaps"); Snowflake IDs are monotonic but not gapless.

A) **Keep both** — `EventId` (Snowflake) for PK/idempotence/sort/cross-app refs; `SequenceNumber` (per-device contiguous) retained for gap-free replication tracking (recommended)

B) Replace the sequence number with the Snowflake `EventId` and change replication completeness to a different mechanism (e.g., per-device high-water mark + explicit gap set)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 10 — Worker/node-ID allocation (Snowflake uniqueness depends on this)
How does each generator (hub, each spoke, cloud) get a unique worker ID?

A) **Assigned at pairing / download**: the hub is the worker-ID authority for its event — reserves one for itself and issues a unique worker ID to each paired spoke with its device credential (FR-4.4); the cloud backend generates globally-scoped entity IDs under its own reserved worker range. Log-event IDs need uniqueness only within the event scope, so this is collision-safe and fully offline-capable after pairing (recommended)

B) **Cloud pre-allocates a globally-unique worker-ID block** to the hub at event download; the hub sub-allocates to spokes — makes every Snowflake ID globally unique across all events, not just event-scoped

C) Statically configured per app/instance (operator-managed config/env)

D) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `application-design/components.md` — component definitions + high-level responsibilities + interfaces (all 5 modules + shared sub-packages)
- [x] Generate `application-design/component-methods.md` — method signatures + purpose + I/O types (no detailed business rules yet)
- [x] Generate `application-design/services.md` — service definitions + orchestration patterns (registration, event download, pairing, scoring pipeline, replication, recovery)
- [x] Generate `application-design/component-dependency.md` — dependency matrix + communication patterns (REST/SignalR/local-queue) + data-flow diagrams (with text alternatives)
- [x] Generate consolidated `application-design/application-design.md`
- [x] Validate design completeness & consistency against FR/NFR and stories; confirm extension applicability (Security/PBT/Resiliency) at design altitude
- [x] Reflect the settled Snowflake identifier strategy (Q8–Q10) in the design artifacts (event model, ID-generation service in shared-sync-core, worker-ID allocation in the pairing/download flow) and add a corresponding requirements decision + NFR (identifier strategy) via a change-request once answers are locked
- [x] Update aidlc-state.md; log approval in audit.md
