# Code Generation Plan — U1 Shared Core

**Stage**: CONSTRUCTION - U1 Shared Core - Code Generation
**Branch**: `unit/u1-shared-core` (all code committed here; merged to main at end-of-unit approval)
**Date**: 2026-07-24
**This plan is the single source of truth for U1 code generation.**

---

## Unit context
- **Unit**: U1 = `EventManager.Domain` + `EventManager.Sync` (pure libraries).
- **Implements (foundational)**: event-sourcing mechanics (FR-4.2), Snowflake (D-26), and the engines behind US-105/106/109/202/210/211, US-307/308 (weigh-in eval), US-311/312/313 (brackets/seeding), US-402/403 (scoring), US-109 (RBAC). U1 owns **0 primary stories** but enables these.
- **Dependencies**: none (root of the graph). Runtime deps: IdGen, ErrorOr, System.Text.Json. Test deps: xUnit, FsCheck.
- **Consumers (not built here)**: `IEventStore` implemented later by U3 (Npgsql) and U4a (SQLite); replication wired by U7.
- **Design inputs**: functional-design/*, nfr-requirements/*, nfr-design/* for U1.

## Code location (greenfield multi-unit, D-07)
```
shared/
  EventManager.Shared.sln
  EventManager.Domain/            EventManager.Domain.csproj
  EventManager.Sync/              EventManager.Sync.csproj
  tests/
    EventManager.Domain.Tests/    EventManager.Domain.Tests.csproj
    EventManager.Sync.Tests/      EventManager.Sync.Tests.csproj
  Directory.Build.props           (shared TFM net10.0, nullable, pinned pkg versions)
```
Application code → workspace root under `shared/`. Markdown summaries → `aidlc-docs/construction/u1-shared-core/code/`.

---

## Generation Steps

- [x] **Step 1 — Project structure setup**
  - Create `shared/EventManager.Shared.sln`, the two library csproj (`net10.0`, nullable enabled), the two xUnit test projects, and `Directory.Build.props` (pinned versions: IdGen, ErrorOr, FsCheck.Xunit; coverage tooling). Add package refs. Wire local NuGet packing metadata on the libraries.

- [x] **Step 2 — Domain: entities & value objects** (`EventManager.Domain`)
  - Immutable records: `EventDefinition, Division, AthleteProfile, Registration, Bracket, Match, WeighIn, CheckIn, DeviceCredential, OrganizerRoleAssignment, PaymentRecord`; VOs: `DivisionCriteria, WeightClass, WeighInPolicy, ScoringConfig, PointSparringConfig, PenaltyPolicy, FormsConfig, Seed, ScoreEntry, MatchOutcome, WeighInOutcome, Recommendation`. Snowflake `EventId`-typed ids. (domain-entities.md)

- [x] **Step 3 — Domain: engines**
  - `IBracketEngine` (single-elim + byes, round-robin) · `ISeedingEngine` (academy separation) · `IScoringEngine`/`IRuleset` (point-sparring w/ configurable `PenaltyPolicy`, forms drop-high/low) · `IWeighInPolicyEvaluator` (tolerance/strict/auto-move) · `IRoleAuthorizationPolicy` (deny-by-default). Expected failures via `ErrorOr<T>` (P-6). (business-logic-model.md, business-rules.md)

- [x] **Step 4 — Sync: event model & serialization** (`EventManager.Sync`)
  - `TournamentEvent` record (EventId, DeviceId, SequenceNumber, EventType, SchemaVersion, Payload, OccurredAt, EventScopeId); `IEventSerializer` + System.Text.Json source-gen impl; upcaster registry (`(EventType,SchemaVersion)` → upcaster, P-5).

- [x] **Step 5 — Sync: Snowflake**
  - `IIdGenerator` + IdGen-backed adapter (`IdStructure(41,10,12)`, epoch 2026-01-01, SpinWait-then-throw); `IWorkerIdRegistry` (event-scoped assignment). (TSD-3)

- [x] **Step 6 — Sync: event store interface, replay, idempotent append**
  - `IEventStore` (AppendIfNotExists, ReadStream, HighWaterMark, ReadAll); `IReplayEngine` (idempotent fold). Interface only for store; in-memory test-double store lives in test project.

- [x] **Step 7 — Sync: projection framework**
  - `IProjection<TState>` + `IProjectionHost` (rebuild by ascending EventId; incremental dispatch). (P-4, Q7)

- [x] **Step 8 — Sync: replication protocol & upcasting**
  - `IReplicationProtocol` (NextBatch by per-device sequence above peer HWM; DetectGaps). (P-10, BR-1.5)

- [x] **Step 9 — Domain tests (FsCheck + xUnit)**
  - Generators (brackets/divisions/weights/score-entries/role-assignments); properties BR-3.x (participant preservation, one champion, byes, academy separation), BR-4.x (scoring determinism, penalty modes, forms drop/tie-break), BR-5.x (tolerance boundary), BR-6.x (RBAC parity); example tests for critical scenarios.

- [x] **Step 10 — Sync tests (FsCheck + xUnit)**
  - In-memory `IEventStore` double; properties BR-1.x (idempotent append/replay, EventId-order determinism, gap-free HWM, serialization round-trip, upcast idempotence), BR-2.x (Snowflake monotonic/no-collision/regression/decode via controllable test clock); projection-oracle test (incremental == full fold).

- [x] **Step 11 — Documentation**
  - `aidlc-docs/construction/u1-shared-core/code/code-summary.md` (files created, story/BR coverage) + `shared/README.md` (build/pack notes, local NuGet feed).

- [x] **Step 12 — Build sanity check (non-gating)**
  - .NET 10 SDK confirmed present (10.0.302): `dotnet build` the shared solution to confirm it compiles. Formal test execution + coverage gate happen in the **Build & Test** phase (after all units), per process.

- [x] **Step 13 — End-of-unit: architecture overview diagrams** (process requirement)
  - Update `aidlc-docs/inception/application-design/architecture-overview.md` to reflect U1 as-built (shared package internals, the IdGen/ErrorOr choices, the Domain/Sync seam).

- [x] **Step 14 — End-of-unit: user testing guide** (process requirement)
  - Author `aidlc-docs/construction/u1-shared-core/user-testing-guide.md` — U1 is a library, so a developer/consumer verification guide: how to build the shared solution, run the FsCheck + xUnit suite, interpret PBT seeds, and what invariants (BR-x) the tests prove.

## Not applicable for U1
- API layer, Repository layer (only `IEventStore` *interface*; adapters are U3/U4a), Frontend, DB migrations, Deployment artifacts — **N/A** (pure library).

## Approval
- [ ] Plan approved by user → proceed to Part 2 (generation)
