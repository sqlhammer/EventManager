# Application Design — Components

**Stage**: INCEPTION - Application Design
**Date**: 2026-07-24
**Altitude**: Component identification, responsibilities, and interfaces. Detailed business rules are deferred to per-unit Functional Design (CONSTRUCTION).
**Design decisions applied**: Q1=C (split Domain/Sync libs), Q2=B (shared interface + shared replay/projection + thin adapters), Q3=A (in-memory projections), Q4=A (shared client-sync lib), Q5a=A (single Admin device), Q5b=A (hub enforces RBAC), Q6=A (shared contracts), Q7=A (MVVM), Q8=B (Snowflake for cross-app IDs), Q9=A (EventId + SequenceNumber), Q10=A (worker IDs assigned at pairing/download).

---

## Module & Package Map (repo layout D-07)

```
shared/                         (four versioned NuGet packages)
  EventManager.Domain           pure domain model + correctness-critical engines
  EventManager.Sync             event-sourcing plumbing (log, replay, projections, Snowflake)
  EventManager.Contracts        DTOs + validation attributes (REST + LAN API)
  EventManager.ClientSync       spoke-side offline queue/replay/reconnect/SignalR client
backend/                        cloud-backend (ASP.NET Core Web API + PostgreSQL, Docker)
admin/                          admin-hub (MAUI app + embedded Kestrel = the LAN hub)
judge/                          judge-app (MAUI spoke)
checkin/                        checkin-app (MAUI spoke)
```

Dependency direction (high level): `Domain` ← `Sync` ← `ClientSync`; `Contracts` → `Domain`. All apps depend on `Domain`, `Contracts`; server + hub depend on `Sync`; spokes depend on `ClientSync`.

---

## 1. `EventManager.Domain` (shared)

**Purpose**: Pure, deterministic domain model and the correctness-critical engines. No I/O, no persistence, no framework dependencies — the heaviest property-based-testing target (NFR-4.3).

| Component | Responsibility | Interface (high level) |
|---|---|---|
| **Domain entities & value objects** | `EventDefinition`, `Division`, `Registrant`/`AthleteProfile`, `Registration`, `Match`, `Bracket`, `Seed`, `WeighIn`, `CheckIn`, `Score`, `DeviceCredential`, `OrganizerRoleAssignment`, `PaymentRecord` — identity via Snowflake IDs (Q8) | Immutable types; factory/`With…` methods |
| **BracketEngine** | Generate single-elimination (with byes) and round-robin structures; extensible for future formats (FR-3.2/3.5) | `IBracketEngine` |
| **SeedingEngine** | Random baseline + academy separation where mathematically possible (FR-3.3) | `ISeedingEngine` |
| **ScoringEngine** | Point-sparring and forms/kata evaluation; pluggable ruleset registry (FR-6.2) | `IScoringEngine`, `IRuleset` |
| **WeighInPolicyEvaluator** | Evaluate a recorded weight against the configured policy (strict/auto-move/tolerance), returning an outcome proposal (FR-5.3) | `IWeighInPolicyEvaluator` |
| **RoleAuthorizationPolicy** | Pure decision: given an `OrganizerRoleAssignment` and an action, is it permitted? Encodes Full-Admin-only set (FR-2.8) | `IRoleAuthorizationPolicy` |

## 2. `EventManager.Sync` (shared)

**Purpose**: Event-sourcing plumbing shared by the hub and cloud (Q2=B: shared interface + shared replay/projection logic; thin persistence adapters live in each module). Owns correctness of append/replay/projection and Snowflake ID generation.

| Component | Responsibility | Interface |
|---|---|---|
| **TournamentEvent** | Immutable, timestamped event record: `EventId` (Snowflake PK/idempotence key), `DeviceId`, `SequenceNumber` (per-device contiguous, Q9), `Type`, `Payload`, `OccurredAt` | record type |
| **IEventStore** | Append-only store abstraction; `AppendIfNotExists`, read by device stream, high-water-mark queries. Implemented by thin per-side adapters (SQLite in hub/spokes, Npgsql in cloud) | `IEventStore` |
| **ReplayEngine** | Idempotent apply/fold of an event stream into projections; guarantees `apply(apply(x)) = apply(x)` (NFR-4.3) | `IReplayEngine` |
| **ProjectionFramework** | In-memory projection registry (Q3=A): builds/updates read models by folding the log; rebuild-on-startup + incremental update | `IProjection<TState>`, `IProjectionHost` |
| **SnowflakeIdGenerator** | Generate 64-bit time-sortable IDs; configured with a worker ID (Q10) and custom epoch; intra-generator monotonic, clock-regression safe | `IIdGenerator` |
| **ReplicationProtocol** | Sequence-ordered replication with per-device high-water mark + gap detection (FR-4.6, US-504); bounded retry/backoff (NFR-3.8) | `IReplicationProtocol` |
| **WorkerIdRegistry** | Track/assign Snowflake worker IDs within an event; hub is authority, cloud uses a reserved range (Q10) | `IWorkerIdRegistry` |

## 3. `EventManager.Contracts` (shared)

**Purpose**: Single source of truth for wire contracts across the cloud REST API and hub LAN API (Q6=A). DTOs plus validation attributes (FluentValidation/DataAnnotations) applied before the event-log write path (NFR-2.4).

| Component | Responsibility |
|---|---|
| **REST DTOs** | Account/auth, event, registration, division, organizer-management, results request/response models |
| **LAN DTOs** | Pairing, scoring, check-in/weigh-in, dispute, device-management, event-download payloads |
| **Event payload contracts** | Strongly-typed payloads for each `TournamentEvent.Type` |
| **Validators** | Validation rules attached to DTOs; shared by server and clients |

## 4. `EventManager.ClientSync` (shared)

**Purpose**: Reusable spoke-side offline behavior (Q4=A), consumed by Judge and Check-In. Encapsulates everything that makes a spoke resilient so the apps only add UI + role-specific flows.

| Component | Responsibility | Interface |
|---|---|---|
| **LocalEventQueue** | Durable-before-ack local SQLite queue of outbound events (NFR-1.1) | `ILocalEventQueue` |
| **SyncClient** | Connect to hub over WSS, redeem pairing credential, send/replay queued events idempotently, track sync status | `ISyncClient` |
| **ReconnectSupervisor** | Auto-reconnect + resync with no user action (US-507, NFR-1.3) | `IReconnectSupervisor` |
| **HubPushConsumer** | Consume SignalR push (bracket/schedule/results updates), apply to local projection | `IHubPushConsumer` |
| **PairingClient** | mDNS discovery + manual-IP/QR fallback; cert-fingerprint pinning; token redemption (FR-4.3/4.4) | `IPairingClient` |

## 5. `backend/` — cloud-backend (ASP.NET Core Web API)

**Purpose**: Pre-event accounts/registration and the cloud mirror of the event log. Docker + PostgreSQL (D-10). Never a conflicting source of truth.

| Component | Responsibility |
|---|---|
| **Identity/Auth** | ASP.NET Core Identity + JWT; MFA for organizers (FR-1.5); breached-password/lockout (NFR-2.6) |
| **AccountController** | Registration/login for organizer, coach, registrant (FR-1.1) |
| **EventController** | Create/edit events; creator gets Full Admin (FR-2.1) |
| **OrganizerController** | Add co-organizers (invite/direct), manage roles, Full-Admin-only actions (FR-2.7/2.8) |
| **RegistrationController** | Athlete/coach/parent registration, edits, payment status (FR-2.2–2.5) |
| **DivisionController** | Division config + automatic assignment (FR-3.1) |
| **EventIngestController** | Receive replicated event batches from the hub; `AppendIfNotExists` (FR-4.6, US-504) |
| **ResultsController** | Post-event results & history read models (FR-1.2, FR-6.5) |
| **PostgresEventStore** | Thin Npgsql adapter implementing `IEventStore` (Q2) |
| **CloudProjectionHost** | Maintains cloud read models from the log |

## 6. `admin/` — admin-hub (MAUI + embedded Kestrel)

**Purpose**: The organizer's app; embeds the LAN hub server; authoritative for bracket/division/schedule on event day; runs fully offline (FR-4.1). Single Admin device per event (Q5a=A). MVVM (Q7=A).

| Component | Responsibility |
|---|---|
| **HubServer** | Embedded Kestrel host: WSS endpoints + SignalR hub; self-signed cert (NFR-2.1) |
| **EventDownloadService** | Download full event to local SQLite; "event-day ready" gate (US-301) |
| **PairingService / DeviceManager** | Issue pairing QRs, one-time tokens, device credentials + roles + Snowflake worker IDs; revoke devices (FR-4.4, US-305/508, Q10) |
| **OrganizerAuthService** | Offline authentication of organizers + hub-side RBAC enforcement (Q5b=A); uses downloaded `OrganizerRoleAssignment`s + `RoleAuthorizationPolicy` |
| **HubEventStore** | Thin SQLite adapter implementing `IEventStore`; SQLCipher-encrypted (NFR-2.3) |
| **HubProjectionHost** | In-memory projections rebuilt on start, updated incrementally (Q3) |
| **BracketService** | Orchestrates `BracketEngine`/`SeedingEngine`; generation, regeneration, advancement (FR-3.2–3.5, FR-6.3) |
| **WeighInPolicyService** | Applies `WeighInPolicyEvaluator` outcomes; triggers division-move regeneration (FR-5.3, US-309) |
| **DisputeService** | Dispute flag intake + organizer resolution as events (FR-6.4) |
| **ReplicationClient** | Hub→cloud replication via `ReplicationProtocol`; retry/backoff (FR-4.6) |
| **BackupService** | Periodic + on-demand encrypted event-log snapshots (FR-4.9) |
| **RecoveryService** | Restore from cloud replica or backup; rebuild by replay (FR-4.8, US-506) |
| **SignalRPushService** | Push bracket/schedule/results to spokes (FR-4.7) |
| **ViewModels/Views** | MVVM surfaces: roster, divisions, brackets, standings, devices, check-in board, disputes |

## 7. `judge/` — judge-app (MAUI spoke)

**Purpose**: Per-mat scoring; offline-first; consumes `ClientSync`. MVVM.

| Component | Responsibility |
|---|---|
| **PairingViewModel** | QR/manual-IP pairing via `PairingClient` (US-303/304) |
| **MatQueueViewModel** | Assigned-mat queue in schedule order (FR-6.1, US-401) |
| **ScoringViewModel** | Point-sparring + forms/kata entry via `ScoringEngine`; durable-before-ack (FR-6.2, US-402/403) |
| **CrossMatViewModel** | Read-only view of other mats when connected (US-410, FR-6.1) |
| **FocusModeController** | Lock UI to current match (US-411, FR-6.6) |
| **DisputeViewModel** | Flag a completed match (FR-6.4, US-405) |
| **SyncStatusViewModel** | Queued-count/sync status surfaced honestly (US-502) |

## 8. `checkin/` — checkin-app (MAUI spoke)

**Purpose**: Check-in and weigh-in; append-only; offline-first; consumes `ClientSync`. MVVM.

| Component | Responsibility |
|---|---|
| **PairingViewModel** | Pairing via `PairingClient` |
| **CheckInViewModel** | Mark present in a couple taps (FR-5.1, US-306) |
| **WeighInViewModel** | Record weight with in/out-of-range feedback via `WeighInPolicyEvaluator` (FR-5.2, US-307) |
| **RecommendationController** | Attach non-binding policy recommendation on out-of-range (D-25, US-307/308) |
| **StatusBoardViewModel** | Per-division checked-in/missing/weighed counts (FR-5.4, US-310) |
| **SyncStatusViewModel** | Queue/sync status (US-503) |

---

## Extension applicability (design altitude)

| Extension | Applicability at Application Design |
|---|---|
| **Security Baseline** | Applicable — component boundaries encode deny-by-default authz (backend + hub `OrganizerAuthService`), append-only log, encrypted stores, shared validation in `Contracts`. Detailed control verification at per-unit gates. |
| **Property-Based Testing** | Applicable — `EventManager.Domain` and `EventManager.Sync` are the mandated PBT surfaces (bracket invariants, replay idempotence, projection oracle). Properties identified per unit in Functional Design. |
| **Resiliency Baseline** | Applicable — `ReplicationProtocol` (retry/backoff), `ReconnectSupervisor`, `BackupService`/`RecoveryService`, health endpoints on `HubServer`/backend map to resiliency obligations. |
