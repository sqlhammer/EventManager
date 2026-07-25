# U1 Shared Core — Testing & Verification Guide

**Unit**: U1 (`EventManager.Domain` + `EventManager.Sync`) — pure libraries, no UI. This is a **developer/consumer verification guide** (the end-of-unit "user testing guide" for a library unit).
**Branch**: `unit/u1-shared-core`

---

## 1. Prerequisites
- .NET 10 SDK (verified: 10.0.302).
- Restore happens automatically on build (NuGet: IdGen, ErrorOr, System.Text.Json, xUnit, FsCheck.Xunit).

## 2. Build
```bash
dotnet build shared/EventManager.Shared.slnx
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

## 3. Run the tests
```bash
dotnet test shared/EventManager.Shared.slnx
```
Expected: **31 passed, 0 failed** (Domain 20, Sync 11). FsCheck `[Property]` tests each run ~100 generated cases.

### Reproducing a property failure (PBT seeds)
FsCheck prints the failing seed and the shrunk counterexample on failure, e.g. `(StdGen (a,b))`. Re-run a single test with the seed to reproduce:
```bash
dotnet test shared/EventManager.Shared.slnx --filter "FullyQualifiedName~BracketPropertyTests"
```
Seeds are logged in CI (PBT-08) so any failure is reproducible.

## 4. What the tests prove (BR invariants)
| Area | Test | Invariant |
|---|---|---|
| Brackets | `BracketPropertyTests` | participant preservation, byes = nextPow2(n)−n, exactly one champion, round-robin completeness (BR-3) |
| Scoring | `ScoringTests` | higher-effective wins, penalty-cap DQ, deduct floor, forms order-independence + drop-high/low (BR-4) |
| Weigh-in | `WeighInRbacTests` | tolerance boundary (% of upper, over-only), strict DQ, auto-move (BR-5) |
| RBAC | `WeighInRbacTests` | deny-by-default, Full-Admin-only set (BR-6) |
| Event log | `ReplayProjectionTests` | idempotent append/replay, EventId-ordered deterministic projections (BR-1) |
| Snowflake | `SnowflakeTests` | monotonic + unique, cross-worker no-collision, clock-regression alarm (BR-2) |
| Replication | `ReplicationTests` | batch above high-water mark, gap detection, contiguous HWM (BR-1.5) |

## 5. How a consumer verifies integration (for later units)
- Reference `EventManager.Domain` / `EventManager.Sync` from your project.
- Implement `IEventStore` against your persistence (U3 Npgsql, U4a SQLite) and re-run the `IEventStore` contract expectations shown in `InMemoryEventStore` tests as a template.
- Feed a real `IIdGenerator` (`new SnowflakeIdGenerator(workerId)`) with a worker id from `WorkerIdRegistry`.

## 6. Coverage
Target 90% line/branch on U1 (Q4=A). Measured formally in the **Build & Test** phase (after all units) — not gated in this unit's compile check.
