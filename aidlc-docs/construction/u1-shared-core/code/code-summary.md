# U1 Shared Core — Code Generation Summary

**Stage**: CONSTRUCTION - U1 Shared Core - Code Generation (Part 2)
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24

## Created — application code (workspace root)
```
shared/
  Directory.Build.props
  EventManager.Shared.slnx
  EventManager.Domain/
    Ids.cs                     Snowflake identity value type
    Enums.cs                   OrganizerRole, BracketFormat, PenaltyMode, WeighInResult, OrganizerAction, ...
    ValueObjects.cs            DivisionCriteria, WeightClass, WeighInPolicy, ScoringConfig, PenaltyPolicy, scoring I/O
    Entities.cs                EventDefinition, Division, Registration, Bracket, Match, WeighIn, ...
    AssemblyInfo.cs            InternalsVisibleTo tests
    Engines/BracketEngine.cs          single-elim (+byes) + round-robin + advancement (BR-3)
    Engines/SeedingEngine.cs          academy-separation seeding (BR-3.6)
    Engines/ScoringEngine.cs          point-sparring (configurable penalties) + forms drop-high/low (BR-4)
    Engines/WeighInPolicyEvaluator.cs tolerance/strict/auto-move (BR-5)
    Engines/RoleAuthorizationPolicy.cs deny-by-default RBAC (BR-6)
  EventManager.Sync/
    TournamentEvent.cs         event atom (EventId/DeviceId/SequenceNumber/SchemaVersion)
    Serialization.cs           IEventSerializer + JsonEventSerializer + UpcasterRegistry (P-1/P-5)
    Ids.cs                     IIdGenerator + IdGen adapter + WorkerIdRegistry (P-2, TSD-3)
    EventStore.cs              IEventStore interface (Q2 seam)
    Replay.cs                  IReplayEngine idempotent fold (P-3)
    Projections.cs             IProjection + ProjectionHost, EventId-ordered rebuild (P-4/Q7)
    Replication.cs             IReplicationProtocol HWM + gap detection (P-10)
  tests/EventManager.Domain.Tests/   TestData, Bracket/Scoring/WeighIn/RBAC property+example tests
  tests/EventManager.Sync.Tests/     InMemoryEventStore, MutableTimeSource, Snowflake/Replay/Projection/Replication tests
```

## Verification
- `dotnet build EventManager.Shared.slnx` → **succeeded, 0 warnings, 0 errors**.
- `dotnet test` → **31 passed / 0 failed** (Domain 20, Sync 11); FsCheck `[Property]` tests each ran ~100 generated cases. Formal coverage-gate measurement (90% target) runs in the Build & Test phase.

## Story / requirement coverage
U1 owns 0 primary stories (foundational). Implements engines/mechanics behind US-105/106/109/202/210/211, US-307/308, US-311/312/313, US-402/403, and FR-4.2 / D-26. BR-1..BR-7 invariants are encoded as tests.

## As-built refinements (noted for design docs)
1. **`EventManager.Sync` is independent of `EventManager.Domain`** — event payloads are opaque bytes at the Sync layer (generic plumbing), which is cleaner/more reusable than the high-level `Sync → Domain` edge shown in `component-dependency.md`. Concrete domain projections live in consuming units. Recorded as an as-built note in the design docs.
2. **Snowflake via IdGen** wrapped behind `IIdGenerator`; clock-regression realized as catch-`InvalidSystemClockException` → bounded wait → `ClockRegressionException` alarm.
3. Tests were executed now (green) for confidence; the formal Build & Test gate still runs after all units.

## Not applicable (pure library)
API layer, repository layer, frontend, DB migrations, deployment artifacts.
