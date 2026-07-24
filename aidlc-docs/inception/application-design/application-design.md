# Application Design — Consolidated

**Project**: EventManager
**Stage**: INCEPTION - Application Design
**Date**: 2026-07-24
**Altitude**: High-level component identification and service-layer design. Detailed business logic and NFR patterns are defined per-unit in CONSTRUCTION (Functional Design, NFR Design, Infrastructure Design).

This document consolidates the Application Design artifact set:
- `components.md` — components, responsibilities, interfaces
- `component-methods.md` — indicative method signatures
- `services.md` — service/orchestration boundaries
- `component-dependency.md` — dependencies + communication patterns
- `architecture-overview.md` — topology + event-flow diagrams

---

## 1. Design decisions locked (application-design-plan.md)

| # | Decision |
|---|---|
| Q1=C | Split shared libraries: `EventManager.Domain` (domain + engines) separate from `EventManager.Sync` (event-sourcing plumbing) |
| Q2=B | Shared `IEventStore` interface + shared replay/projection logic; thin per-side persistence adapters (SQLite hub/spokes, Npgsql cloud) |
| Q3=A | In-memory projections rebuilt on startup, updated incrementally (cold start must meet NFR-5.3 < 30s) |
| Q4=A | Shared `EventManager.ClientSync` library for spoke offline behavior |
| Q5a=A | Single Admin/hub device on event day (no admin-client capability in MVP) |
| Q5b=A | Hub enforces organizer RBAC offline (Security-Baseline compliant); offline auth mechanism deferred to Functional Design |
| Q6=A | Shared `EventManager.Contracts` package (DTOs + validators) — single source of truth |
| Q7=A | MVVM for all MAUI client apps |
| Q8=B | Snowflake IDs for all cross-app identifiers; local-only surrogate keys unchanged |
| Q9=A | Keep both `EventId` (Snowflake) and per-device contiguous `SequenceNumber` |
| Q10=A | Snowflake worker IDs assigned at pairing/download (hub authority; cloud reserved range; event-scoped uniqueness) |

## 2. System shape

Four shared NuGet packages + four deployable modules, per the D-07 layout:

```
shared/  EventManager.Domain | EventManager.Sync | EventManager.Contracts | EventManager.ClientSync
backend/ cloud-backend (ASP.NET Core Web API + PostgreSQL, Docker)
admin/   admin-hub (MAUI + embedded Kestrel = LAN hub)
judge/   judge-app (MAUI spoke)
checkin/ checkin-app (MAUI spoke)
```

- **Domain** is dependency-free (heaviest PBT target).
- **Sync** owns log/replay/projection correctness + Snowflake generation.
- **ClientSync** gives Judge/Check-In their offline resilience for free.
- **Admin** is the hub (not a spoke) — owns authority, projections, replication, backup/recovery, and offline RBAC.
- **Backend** is a mirror + pre-event registration/accounts.

(See `component-dependency.md` for the full graph — acyclic, `Domain` at the sink.)

## 3. Services (orchestration boundaries)

| Service | Flow | Key stories |
|---|---|---|
| S-1 Registration (cloud) | online sign-up + division assignment + stubbed payments | US-201–210 |
| S-2 Organizer & RBAC (cloud+hub) | accounts, co-organizers, role enforcement both sides | US-101–103, 108, 109 |
| S-3 Event Download & Readiness (hub) | make hub event-day ready, zero-internet | US-301 |
| S-4 Pairing & Device (hub↔spoke) | enrollment, roles, worker IDs, revocation | US-303–305, 508 |
| S-5 Check-In/Weigh-In (spoke→hub) | presence, weight, policy + recommendations | US-306–310 |
| S-6 Scoring & Advancement (spoke→hub) | scoring, mat authority, bracket advance, disputes, live results | US-401–411 |
| S-7 Replication (hub→cloud) | async sequence-ordered mirror + completeness | US-504, 602 |
| S-8 Backup & Recovery (hub) | backups + manual hub recovery | US-505, 506 |

## 4. Identifier strategy (Snowflake)

- Every cross-app identifier is a 64-bit Snowflake, minted at its origin: **cloud** for pre-event entities (accounts, events, registrations, divisions), **hub/spokes** for event-day entities and log events.
- `TournamentEvent` carries `EventId` (Snowflake, PK + idempotence key + sort) **and** `DeviceId` + per-device contiguous `SequenceNumber` (gap-free replication).
- Worker IDs are assigned at pairing (hub authority) with the cloud on a reserved range; log-event uniqueness is required only within an event's scope.
- Best-effort cross-device time ordering (bounded by clock accuracy); authoritative per-stream order is the sequence number.

## 5. Traceability at design altitude

- Every FR area maps to at least one component/service (see `components.md` and `services.md` cross-references to FR/US IDs).
- Correctness-critical logic is concentrated in `Domain` + `Sync` — the mandated PBT surfaces (NFR-4.3).
- Security/authz boundaries are explicit: deny-by-default in `backend`, and the hub's `OrganizerAuthService` enforcing the same `RoleAuthorizationPolicy` offline.

## 6. Extension compliance (design altitude)

| Extension | Status | Rationale |
|---|---|---|
| Security Baseline | **Compliant (design)** | Server-side role checks on both cloud and hub (Q5b); append-only log; encrypted stores (SQLCipher/PG); shared validation in `Contracts`; pairing/cert-pinning components. Control-level verification at per-unit gates. |
| Property-Based Testing | **Compliant (design)** | `Domain` + `Sync` isolated as pure, testable surfaces; properties (bracket invariants, replay idempotence, projection oracle, seeding) identified per-unit in Functional Design. |
| Resiliency Baseline | **Compliant (design)** | `ReplicationProtocol` retry/backoff, `ReconnectSupervisor`, `BackupService`/`RecoveryService`, health endpoints; graceful degradation is the core premise. |

## 7. Deferred to CONSTRUCTION
- Offline organizer authentication mechanism (Q5b note) → Functional Design.
- Detailed business rules for engines (bracket edge cases, scoring tie-breaks, tolerance math) → Functional Design.
- Snowflake bit-layout finalization, projection schemas, DTO fields → Functional Design.
- Deployment topology specifics (Compose, health checks, secrets) → Infrastructure Design.

## 8. Requirements deltas raised by this stage
Two decisions + supporting NFR/FR notes were added to `requirements.md` via change-request (logged in `audit.md`):
- **D-26** — Snowflake identifier strategy (cross-app IDs; EventId + SequenceNumber; worker-ID allocation).
- **D-27** — Event-day topology = single Admin device; hub enforces organizer RBAC offline.
