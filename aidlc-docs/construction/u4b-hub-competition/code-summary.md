# U4b Hub Competition — Code & Verification Summary

**Stage**: CONSTRUCTION → Code Generation (fast-tracked) · **Unit**: U4b Hub Competition
**Date**: 2026-07-25 · **Builds green; 12 hub tests passing (5 U4a + 7 U4b).** Branch `unit/u4b-hub-competition`.
Code from this unit onward follows coding standard **CS-1 (no ternary `?:`)**.

## What shipped
Added a `Competition/` module inside `admin/EventManager.Hub` (same hub assembly as U4a). It orchestrates the **U1 domain engines** on the hub — no new UI, fully headless/testable.

| Area | Files | Stories |
|---|---|---|
| Read models | `CompetitionEntities` (BracketRow, StandingRow, DisputeRow, DivisionStatusRow, MatchDto + `BracketMapper`); `HubDbContext` extended | — |
| Events | `CompetitionEvents` (BracketGenerated/Advanced, DivisionStarted, WeighInResolved, DivisionMoved, DisputeFlagged/Resolved, DivisionFinalized) | audit/replication |
| Services | `BracketService` (seed→generate→advance→start, regen guard), `ScoringIntakeService` (mat authority + score→advance), `WeighInResolutionService` (evaluate + move), `DivisionFinalizationService`, `DisputeService` | US-311/312/313/314, 404, 405, 406, 308, 309, 601, 408 |
| API | `CompetitionController` (advance/start/finalize/standings/disputes/mat-assignment); `DeviceRegistry.AssignMatAsync` (mat authority) | — |

## Engines orchestrated (from U1, already PBT-covered)
`SeedingEngine` (academy separation), `BracketEngine` (single-elim/round-robin/advance), `ScoringEngine` (point-sparring/forms), `WeighInPolicyEvaluator` (strict/auto-move/tolerance).

## Design note
Competition read models are updated **transactionally by the services** alongside the audit/replication event append (the hub log stays the source for replication; a full log-rebuild of competition projections is a documented follow-up). Bracket state is persisted as serialized `MatchDto[]` and reconstructed to the U1 `Bracket` for advancement.

## Tests (7 U4b)
Bracket generation creates bracket + standings; regeneration blocked after start (US-314); advance records a win (US-404); **foreign-mat score rejected** (US-406); assigned-mat score advances; strict weigh-in over limit disqualifies (US-308); finalize assigns placements by wins (US-601).

## Deferred (documented)
Live standings push to spokes uses the U4a `IHubPush` seam (SignalR concrete deferred); check-in status board fold (US-310) is minimal (DivisionStatusRow exists; population wires with U6). Bracket-regeneration-on-move (US-309) emits `DivisionMoved`; the caller re-invokes `BracketService.GenerateAsync` with the updated field.

## Verify
```bash
dotnet build admin/EventManager.Admin.slnx
dotnet test  admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj   # 12 passed
```
