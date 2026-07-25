# Units of Work — EventManager

**Stage**: INCEPTION - Units Generation (Part 2)
**Date**: 2026-07-24
**Decomposition decisions**: Q1=A (two shared units), Q2=B (split hub), Q3=B (dedicated resilience unit), Q4=A/Q7=A (build order), Q5=B (payment unit), Q6=A (U7 subsumes hub resilience; primitives stay in shared libs).

A **unit of work** is a logical grouping of stories designed and built together. Greenfield code organization follows the D-07 simulated multi-repo layout (`shared/`, `backend/`, `admin/`, `judge/`, `checkin/`); units map onto packages/modules within that layout.

---

## Unit set (9 units)

### U1 — Shared Core  *(shared libs: `EventManager.Domain` + `EventManager.Sync`)*
**Responsibility**: The correctness core. Domain entities/value objects and the pure engines (bracket, seeding, scoring, weigh-in policy, RBAC policy); event log, `IEventStore` interface, idempotent replay, in-memory projection framework, Snowflake `IIdGenerator`, `ReplicationProtocol` + `WorkerIdRegistry` **types**.
**Nature**: Foundational library — critical path, **heaviest PBT target** (NFR-4.3). No user-facing stories; underpins nearly all of them.
**Code**: `shared/EventManager.Domain`, `shared/EventManager.Sync`.

### U2 — Contracts & Client Sync  *(shared libs: `EventManager.Contracts` + `EventManager.ClientSync`)*
**Responsibility**: Wire DTOs + validators (single source of truth for REST + LAN APIs); spoke-side offline behavior library (`LocalEventQueue`, `SyncClient`, `ReconnectSupervisor`, `HubPushConsumer`, `PairingClient`).
**Nature**: Foundational library consumed by backend, hub, and both spokes.
**Code**: `shared/EventManager.Contracts`, `shared/EventManager.ClientSync`.

### U3 — Cloud Backend  *(service: `backend/`)*
**Responsibility**: Pre-event accounts/auth (+MFA), organizer RBAC management, registration (self/parent/coach-bulk), division config + assignment, event creation, replication ingest, results/history read models. Docker + PostgreSQL.
**Nature**: Independently deployable service (Medium criticality).
**Code**: `backend/`.

### U4a — Hub Core  *(module in `admin/`)*
**Responsibility**: Embedded Kestrel server (WSS + SignalR transport, mDNS), device pairing & management incl. revocation + worker-ID assignment, event download & readiness, offline organizer authentication + hub-side RBAC, projection-host infrastructure, health endpoint.
**Nature**: Hub foundation (Critical). Built before U4b.
**Code**: `admin/` (core module).

### U4b — Hub Competition  *(module in `admin/`)*
**Responsibility**: Bracket generation/regeneration/advancement, seeding orchestration, scoring intake + mat-authority enforcement, weigh-in policy resolution, dispute resolution, live standings/results, check-in status board, division finalization.
**Nature**: Hub domain application (Critical). Depends on U4a + U1.
**Code**: `admin/` (competition module).

### U5 — Judge App  *(app: `judge/`)*
**Responsibility**: Pairing, assigned-mat queue, point-sparring + forms scoring (durable-before-ack), read-only cross-mat view, focus/lock mode, dispute flagging, sync-status surface.
**Nature**: MAUI spoke app.
**Code**: `judge/`.

### U6 — Check-In App  *(app: `checkin/`)*
**Responsibility**: Pairing, check-in, weigh-in with range validation, non-binding policy recommendations, per-division status board, sync-status surface.
**Nature**: MAUI spoke app.
**Code**: `checkin/`.

### U7 — Offline Resilience  *(cross-cutting; code lands in `shared/`, `admin/`, `judge/`, `checkin/`)*
**Responsibility**: The flagship offline behavior end-to-end (Epic 5): hub→cloud replication + outage replay + completeness verification, hub local backup export, manual hub recovery, spoke offline queue/replay **integration**, spoke auto-reconnect/resync, and the zero-internet full-event property. Owns the resilience-spanning **integration + PBT/integration tests**; uses primitives defined in U1/U2 (Q6=A — types stay in the shared libs).
**Nature**: Cross-cutting integration unit (Critical). Integrates after the hub exists; spokes consume its behavior.
**Code**: touches `shared/` (wiring), `admin/` (replication/backup/recovery), `judge/`+`checkin/` (queue/reconnect integration).

### U8 — Payment Stub  *(module in `backend/`)*
**Responsibility**: The stubbed/mocked payment-provider abstraction (D-06) — pay-at-door tracking always; card path against a mock provider with decline/timeout paths; no live Stripe.
**Nature**: Small module consumed by U3 registration.
**Code**: `backend/` (payment abstraction module).

---

## Code organization strategy (greenfield, D-07)

```
shared/
  EventManager.Domain/         U1
  EventManager.Sync/           U1
  EventManager.Contracts/      U2
  EventManager.ClientSync/     U2
backend/                       U3  (+ U8 payment module, + resilience ingest wiring for U7)
admin/                         U4a + U4b  (+ U7 replication/backup/recovery)
judge/                         U5  (+ U7 queue/reconnect integration)
checkin/                       U6  (+ U7 queue/reconnect integration)
```

Each top-level folder is its own .NET solution; shared packages are versioned and consumed via a local NuGet feed. U7 is the one unit that deliberately spans folders — it is an *integration* unit, and its cross-folder footprint is the accepted cost of choosing a dedicated resilience unit (Q3=B).

---

## Notes on boundaries
- **U1/U2 are foundational** — they own no user-facing story exclusively but implement the mechanisms nearly every story depends on (see story map's "enables" column).
- **U7 owns integration, not primitives** (Q6=A): `ReplicationProtocol`, `LocalEventQueue`, `IEventStore`, `ReplayEngine` are declared in U1/U2; U7 wires and hardens them across the hub and spokes and owns the E5 stories + resilience test suites.
- **US-508 (device revocation)** is grouped in Epic 5 but is a device-management concern → owned by **U4a**, with U7 depending on revocation semantics during recovery.
