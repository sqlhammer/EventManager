# Unit Test Execution

**Verified**: 2026-07-27 on `main` — **153 passing, 0 failing, 0 skipped** across 9 test assemblies.

---

## Run all tests

```bash
dotnet test shared/EventManager.Shared.slnx        # 42
dotnet test backend/EventManager.Backend.slnx      # 83
dotnet test admin/EventManager.Admin.slnx          # 17
dotnet test judge/EventManager.Judge.slnx          #  6
dotnet test checkin/EventManager.Checkin.slnx      #  5
```

## Expected results

| Assembly | Unit | Tests | Covers |
|---|---|---|---|
| `EventManager.Domain.Tests` | U1 | 20 | Bracket/seeding/scoring engines, weigh-in policy evaluator, RBAC policy |
| `EventManager.Sync.Tests` | U1 | 11 | Event log, idempotent replay, replication protocol |
| `EventManager.Contracts.Tests` | U2 | 4 | Envelope serialization round-trips |
| `EventManager.ClientSync.Tests` | U2 | 7 | Local event queue, transport seam |
| `EventManager.Payments.Tests` | U8 | 6 | Stub payment provider outcomes |
| `EventManager.Api.Tests` | U3 + U9 | 77 | Registration, RBAC, ingest, division eligibility, account deletion, **U9 read tiers / shapes / non-disclosure / ETags / properties** |
| `EventManager.Hub.Tests` | U4a/U4b/U7 | 17 | Pairing, device registry, competition orchestration, replication, backup/recovery |
| `EventManager.Judge.Core.Tests` | U5 | 6 | Durable-before-ack score capture, mat queue |
| `EventManager.Checkin.Core.Tests` | U6 | 5 | Check-in, weigh-in range validation |
| **Total** | | **153** | |

Runtime is roughly 12 seconds in total; `EventManager.Api.Tests` dominates at ~6 s because the
property tests each stand up a fresh in-memory SQLite host.

---

## Property-based tests

FsCheck runs alongside xUnit via `FsCheck.Xunit`; properties are `[Property]`-attributed rather
than `[Fact]`.

**Mandatory properties** (NFR-4.3, U3-NFR-T2):

| Property | Where |
|---|---|
| PBT-1 division-assignment determinism / order-independence | `DivisionEligibilityTests` |
| PBT-2 no double-registration across batches and resubmits | `RegistrationServiceTests` |
| PBT-3 RBAC deny-by-default | `RbacTests` |
| PBT-4 ingest idempotency — any order/partition/repetition yields identical log + projection | `IngestServiceTests` |
| U9 P1–P6 — deny-by-default, shape confinement, query oracle, conditional stability, tier monotonicity, cross-scope isolation | `ReadPropertyTests` |
| Replay idempotence, bracket invariants, seeding invariants | `EventManager.Domain.Tests`, `EventManager.Sync.Tests` |
| Zero-internet full-event property | `EventManager.Hub.Tests` |

**Shrinking is enabled** (framework default, never overridden). On failure FsCheck prints the
shrunk minimal counterexample and the seed. Reproduce a specific failure by pinning the seed:

```bash
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~ReadPropertyTests"
```

Per PBT-08, a failing property must be **investigated, not retried**. When one finds a real defect,
add the shrunk case as a permanent example-based regression test (PBT-10).

---

## Coverage

Target is **80%+ on core logic** (NFR-4.1, U3-NFR-T1) — event-sourcing engine, idempotent replay,
conflict handling, registration/assignment/RBAC/ingest projection. Plumbing is deliberately lighter.

```bash
dotnet test backend/EventManager.Backend.slnx --collect:"XPlat Code Coverage"
```

Reports land in `backend/tests/*/TestResults/<guid>/coverage.cobertura.xml`.

> **Gap**: CI collects coverage but the **threshold gate is not wired** — see the placeholder comment
> in `.github/workflows/backend.yml`. NFR-4.4 requires coverage to block merge, so this is an
> outstanding item, not a completed control.

---

## Filtering

```bash
# One class
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~ReadTierTests"

# Only the U9 read surface
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~Read"

# One test
dotnet test admin/EventManager.Admin.slnx --filter "DisplayName~zero_internet"
```

---

## If tests fail

1. Read the failure output — FsCheck failures include the shrunk input and seed; xUnit failures
   include the assertion diff.
2. Determine whether the **test** or the **code** is wrong. Both happen: during U9 a Postman
   assertion used `headers.get(...) == null`, which can never hold because a missing header reads
   as `undefined` — the API was correct and the assertion was not.
3. Fix, re-run the affected assembly, then re-run all five solutions before committing.

## Test host pattern

Services are tested against **in-memory SQLite**, not mocks — the event stores are
provider-agnostic and use no raw SQL, so the real components run unchanged. Reuse `TestHost`
(backend) and `HubTestHost` (admin) rather than constructing dependencies by hand; both expose
seeding helpers such as `SeedOpenEventAsync`, `RegisterAsync`, and `SeedIdentityAsync`.
