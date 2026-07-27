# Build and Test Summary

**Stage**: CONSTRUCTION → Build and Test (project-level, after all units)
**Executed**: 2026-07-27 on `main`, commit `34d866d`

---

## Build Status

| Field | Value |
|---|---|
| **Build tool** | .NET SDK 10.0.302 |
| **Solutions** | 5 (`shared`, `backend`, `admin`, `judge`, `checkin`) |
| **Status** | ✅ **Success** — 0 errors, **0 warnings** |
| **Artifacts** | Per-project `bin/Debug/net10.0/`; MAUI heads at `bin/Debug/net10.0-windows10.0.19041.0/` |
| **Container** | `backend-api` image builds from repo root via `backend/EventManager.Api/Dockerfile` |

---

## Test Execution Summary

### Unit tests — ✅ Pass

| Assembly | Unit | Tests |
|---|---|---|
| `EventManager.Domain.Tests` | U1 | 20 |
| `EventManager.Sync.Tests` | U1 | 11 |
| `EventManager.Contracts.Tests` | U2 | 4 |
| `EventManager.ClientSync.Tests` | U2 | 7 |
| `EventManager.Payments.Tests` | U8 | 6 |
| `EventManager.Api.Tests` | U3 + U9 | 77 |
| `EventManager.Hub.Tests` | U4a/U4b/U7 | 17 |
| `EventManager.Judge.Core.Tests` | U5 | 6 |
| `EventManager.Checkin.Core.Tests` | U6 | 5 |
| **Total** | | **153 passed, 0 failed, 0 skipped** |

Property-based tests run alongside example-based tests via FsCheck.Xunit, with shrinking enabled and
seeds logged on failure. All mandatory properties from NFR-4.3 and U3-NFR-T2 are implemented, plus
U9's P1–P6.

**Coverage**: collected via `--collect:"XPlat Code Coverage"`. Target is 80%+ on core logic
(NFR-4.1). ⚠️ The **threshold gate is not wired into CI** — NFR-4.4 requires it to block merge, so
this control is specified but not enforced.

### Integration tests — ⚠️ Manual only

No automated suite. Per NFR-4.4 this is a **post-MVP recommendation rather than a required gate**, so
it matches the approved plan. Five manual scenarios are documented; cross-unit interactions that do
not cross a process boundary are already covered in-process by unit tests.

**Two scenarios are blocked**: hub→cloud replication over HTTP, and the live offline-first loop that
depends on it. Both wait on the same missing piece — the `ICloudReplicationTransport` HTTP adapter.

### Performance tests — ❌ Not executed

No load test has been run. Targets are recorded (p95 < 500 ms for registration/login, hundreds of
concurrent users, 300 athletes / ~20 LAN devices) and a k6 approach is specified, but **there is no
measurement**, so no pass/fail can be claimed.

### Security tests — ⚠️ Partially automated

Authorization is well covered: deny-by-default, non-disclosure, IDOR resistance, and RBAC are
asserted by both example-based and property-based tests. Authentication, validation, headers, and
rate limiting have documented manual procedures.

Outstanding: CI dependency scanning, SBOM generation, centralized logging, security alerting and
retention.

### Contract tests — N/A

The hub↔cloud contract is shared as a **compiled library** (U2 `EventManager.Contracts`), not an
independently versioned service API, so consumer-driven contract testing does not apply. Envelope
compatibility is covered by round-trip property tests, and forward-compatibility by the ingest rule
that unknown event types are ignored (BR-ING-3).

### End-to-end tests — ❌ Blocked

The MAUI heads are compiling template shells with no real UI, so no user-workflow e2e is possible.
Spoke behaviour is verified at the app-core layer instead.

---

## Overall Status

| | |
|---|---|
| **Build** | ✅ Success |
| **Unit tests** | ✅ 153/153 pass |
| **Integration** | ⚠️ Manual, two scenarios blocked |
| **Performance** | ❌ Not executed |
| **Security** | ⚠️ Authorization strong; operational controls outstanding |
| **Ready for Operations** | **Not yet** — see below |

**Honest assessment**: the code is in good shape and the automated suite is genuinely strong for a
system of this size, particularly around authorization and offline correctness. What is missing is
almost entirely **operational**: nothing has been load-tested, nothing has run over a real network
boundary between hub and cloud, and the production controls SECURITY-10/14 require (SBOM, scanning,
centralized logging, alerting) are not in place. That is consistent with Operations still being a
placeholder phase — but it means "all tests pass" should not be read as "ready to deploy".

---

## Prioritized next steps

1. **HTTP replication adapter** for `ICloudReplicationTransport` — unblocks two integration
   scenarios and is the single highest-value item. Both ends already exist, so it is buildable now
   with no external decisions.
2. **Wire the CI coverage gate** — NFR-4.4 specifies it and the placeholder is already in the
   workflow.
3. **Run the registration-burst load test** — the only stated latency target, currently unmeasured.
4. **CI dependency scan + SBOM** — SECURITY-10, placeholder already in the workflow.
5. **Centralized logging and security alerting** — SECURITY-03/14, properly an Operations concern.

Items 1–4 are unblocked. Real SMTP, real payments, SQLCipher, non-Windows MAUI heads, and the
Blazor web portal all remain blocked on decisions or toolchain and are excluded from this list.

---

## Generated files

- `build-instructions.md`
- `unit-test-instructions.md`
- `integration-test-instructions.md`
- `performance-test-instructions.md`
- `security-test-instructions.md`
- `build-and-test-summary.md` (this file)
