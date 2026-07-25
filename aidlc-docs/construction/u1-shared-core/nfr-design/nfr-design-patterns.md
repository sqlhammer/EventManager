# U1 Shared Core — NFR Design Patterns

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Purpose**: Design patterns that realize U1's NFRs. Each traces to a U1-NFR ID and/or a decision (Qn/TSD/BR).

---

## P-1 — Serialization Strategy (`IEventSerializer`)
Strategy pattern: `IEventSerializer` interface with a System.Text.Json source-generated implementation (TSD-2). Keeps payloads swappable (MessagePack deferred) without touching the log/replay code. → U1-REL-4, U1-PERF-3.

## P-2 — ID-generator Adapter (`IIdGenerator`)
Adapter wrapping **IdGen** (TSD-3): `IdStructure(41,10,12)`, epoch 2026-01-01, SpinWait-then-throw regression. U1 owns the interface + config + `WorkerIdRegistry` feeding IdGen's `generatorId`. → U1-REL-5, BR-2.x.

## P-3 — Idempotent append (dedupe-on-EventId)
`AppendIfNotExists` checks `EventId` presence before append; duplicate replays are no-ops. → U1-REL-1, BR-1.2/1.3.

## P-4 — Reducer/fold projections (deterministic)
Projections are pure reducers `apply(state, event) → state`. `IProjectionHost` folds the log in **ascending EventId order** (Q7) for `Rebuild`, and dispatches incrementally for live updates. Projection-oracle test asserts incremental == full fold. → U1-REL-2, U1-PERF-2, U1-TEST-5, BR-1.4.

## P-5 — Upcaster pipeline
Chain of version upcasters keyed by `(EventType, SchemaVersion)`; applied on read only; stored events never mutated (Q9). → U1-REL-4, BR-1.7.

## P-6 — Result pattern via **ErrorOr** (Q1=A)
Expected domain outcomes (invalid input, policy rejection, illegal bracket op) return `ErrorOr<T>` (ErrorOr library); exceptions reserved for programmer/exceptional errors. No control-flow-by-exception on hot paths; failures are explicit and property-testable. → maintainability, U1-TEST.

## P-7 — Concurrency contract (Q2=A)
- `IIdGenerator` is thread-safe (IdGen).
- Domain engines are **stateless & pure** → inherently thread-safe.
- `IEventStore` / `IProjectionHost` document a **single-writer model** (consumers serialize writes) — matches the hub's authoritative single-writer role; readers may read projected snapshots.
→ U1-MAINT, U1-PERF.

## P-8 — Pure RBAC policy (Strategy)
`IRoleAuthorizationPolicy.IsPermitted` is a pure function; the identical instance is used by cloud (U3) and hub (U4a) so authz can't diverge. Deny-by-default. → U1-SEC-2, BR-6.x.

## P-9 — Append-only immutability discipline
Entities/events are immutable records; no in-place mutation API; corrections are new events. → U1-SEC-1, BR-1.1.

## P-10 — Replication high-water-mark + gap detection
`IReplicationProtocol` batches by per-device `SequenceNumber` above the peer's high-water mark; `DetectGaps` finds non-contiguous ranges. Enables resume-with-no-gaps/duplicates. → U1-REL-3, BR-1.5 (U7 owns the cross-cutting wiring/tests).

## P-11 — PBT harness
FsCheck generators for events/brackets/divisions/weights/score-entries/role-assignments; shrinking on; **seeds logged in CI**; example-based tests pin critical scenarios. → U1-TEST-1/2/3.

---

## Infra patterns — N/A for U1
Circuit breakers, retries/backoff, caches, queues, rate limiters, bulkheads, scaling/replicas, health checks: **not applicable** — U1 is a pure library with no I/O. These are realized in consuming units (U3 cloud, U4a hub, U7 resilience).

## Trace summary
P-1→U1-REL-4 · P-2→U1-REL-5/BR-2 · P-3→U1-REL-1 · P-4→U1-REL-2/U1-TEST-5 · P-5→BR-1.7 · P-6→Q1/ErrorOr · P-7→Q2 · P-8→U1-SEC-2 · P-9→U1-SEC-1 · P-10→U1-REL-3 · P-11→U1-TEST.
