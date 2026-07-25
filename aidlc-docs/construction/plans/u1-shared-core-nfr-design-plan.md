# NFR Design Plan — U1 Shared Core

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24

**Determined by prior stages (not re-asked)**: serialization Strategy seam (`IEventSerializer`, System.Text.Json impl); IdGen Adapter behind `IIdGenerator`; deterministic fold/reducer projection framework (EventId-ordered); idempotent append (dedupe on `EventId`); upcaster pipeline; FsCheck PBT harness with generators + shrinking; immutable records / pure engines.
**N/A for U1 (pure library — no infra)**: queues, caches, circuit breakers, rate limiters, scaling/replicas, health checks — these belong to consuming units (U3/U4a/U7).

Two open design choices remain. Answer each `[Answer]:` tag.

---

### Question 1 — Domain error-handling pattern
How do engines/validators report *expected* failures (invalid input, policy rejection, illegal bracket op)?

A) **Result/Either pattern** for expected domain outcomes + exceptions reserved for truly exceptional/programmer errors — explicit, testable, no control-flow-by-exception on hot paths (recommended)

B) **Exceptions for all validation failures** — conventional, simpler call sites, but couples control flow to exceptions

C) Other (please describe after [Answer]: tag below)

[Answer]: A. I'd like to use a library such as ErrorOr, not roll our own. You may suggest a different library or accept ErrorOr.

### Question 2 — Concurrency / thread-safety contract for U1
What thread-safety does U1 guarantee to its consumers?

A) **`IIdGenerator` thread-safe (IdGen is); domain engines are stateless & thread-safe (pure); `IEventStore`/`IProjectionHost` document a single-writer model** (consumers serialize writes) — matches the hub's authoritative single-writer role (recommended)

B) Make everything fully concurrent/thread-safe, including projections (locks/immutable snapshots) — heavier, not needed for a single-writer hub

C) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `construction/u1-shared-core/nfr-design/nfr-design-patterns.md` — patterns realizing each NFR (serialization Strategy, IdGen Adapter, reducer/fold projections, idempotent-append dedupe, upcaster pipeline, error-handling pattern, concurrency contract, PBT harness) traced to U1-NFR IDs
- [x] Generate `construction/u1-shared-core/nfr-design/logical-components.md` — the seams/abstractions (`IEventStore`, `IEventSerializer`, `IIdGenerator`, `IProjection`/`IProjectionHost`, `IReplayEngine`, `IReplicationProtocol`, `IWorkerIdRegistry`) + which are U1 interfaces vs consumer-implemented; N/A infra noted
- [x] Update aidlc-state.md; log approval in audit.md (ErrorOr adopted for P-6; added to dependency summary)
