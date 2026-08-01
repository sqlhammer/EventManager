# U10 HTTP Replication Adapter — Code Summary

**Unit**: U10 · **Branch**: `unit/u10-http-replication` · **Stage**: Code Generation (Part 2 complete)
**Plan**: `construction/plans/u10-http-replication-code-generation-plan.md` — all 36 steps `[x]`

---

## 1. Verification results

### Tests: 153 → **202**, zero regressions

| Solution | Before | After | Delta |
|---|---|---|---|
| `shared/EventManager.Shared.slnx` | 42 | 42 | — (untouched, as designed) |
| `backend/EventManager.Backend.slnx` | 83 | **99** | +16 |
| `admin/EventManager.Admin.slnx` | 17 | **44** | +27 |
| `judge/EventManager.Judge.slnx` | 6 | 6 | — |
| `checkin/EventManager.Checkin.slnx` | 5 | 5 | — |
| `EventManager.Integration.slnx` *(new)* | — | **6** | +6 |
| **Total** | **153** | **202** | **+49** |

**The 17 pre-existing admin tests are green across the `ReplicationClient` rewrite** — the gate that
mattered most, since that class carries the flagship zero-data-loss guarantee.

### Build warnings: **2, and the quality gate is NOT met**

The plan's gate was 0 warnings across all six solutions. Reported plainly rather than glossed:

```
admin/EventManager.Hub/Resilience/BackupRecovery.cs(74,25): warning SYSLIB0060
admin/EventManager.Hub/Resilience/BackupRecovery.cs(95,25): warning SYSLIB0060
```

- **Pre-existing, and verified as such** — building the branch with all U10 changes stashed produces the same two warnings. U7's `BackupRecovery.cs` is not touched by this unit.
- They come from an SDK that now flags the obsolete `Rfc2898DeriveBytes` constructors.
- **Not fixed here**, deliberately: it is backup-crypto code outside this unit's scope, and changing key derivation without tests specifically covering derivation equivalence is exactly the scope creep this unit should avoid. The mechanical fix (`Rfc2898DeriveBytes.Pbkdf2`) is behaviour-identical and would suit a small follow-up.
- **Caveat worth knowing**: an *incremental* build reports 0 warnings because the file is not recompiled. `--no-incremental` is required to see the true count.

### Other gates

| Gate | Result |
|---|---|
| 153-test baseline holds | ✅ 202, no regressions |
| 17 admin tests green across the U7 edit | ✅ |
| CS-1 — no ternary operators in new code | ✅ verified by scan (detector sanity-checked against a known ternary) |
| No file under `shared/` modified | ✅ |
| No duplicate or `_modified` files | ✅ |
| Both Postman representations agree | ✅ 13 requests each, identical scripts, all YAML parses |

---

## 2. Files created

**Cloud (`backend/EventManager.Api`)**
- `Services/HubCredentialService.cs`, `Auth/HubCredentialAuth.cs`, `Auth/IngestPolicy.cs`
- `Controllers/HubCredentialController.cs`
- `Persistence/Migrations/20260801204901_HubCredentials.cs` (+ designer, snapshot)

**Hub (`admin/EventManager.Hub`)**
- `Resilience/ReplicationOptions.cs`, `SecretProtection.cs`, `HubCredentialStore.cs`,
  `ReplicationFailure.cs`, `ReplicationCircuitBreaker.cs`, `ReplicationSignal.cs`,
  `ReplicationStatus.cs`, `ReplicationMetrics.cs`, `HttpCloudReplicationTransport.cs`
- `Controllers/ReplicationController.cs`

**Tests**
- `backend/tests/.../HubCredentialTests.cs` · `admin/tests/.../ReplicationAdapterTests.cs`
- `tests/EventManager.Integration.Tests/` (`CloudFixture.cs`, `CredentialPathTests.cs`, csproj)

**Infrastructure / config**
- `EventManager.Integration.slnx` · `backend/otel-collector-config.yaml`
- Postman: 13 requests in both representations

## 3. Files modified

`Persistence/Entities.cs`, `AppDbContext.cs`, `Services/IngestService.cs`,
`Controllers/EventIngestController.cs`, `Program.cs` (cloud) · `Persistence/HubEntities.cs`,
`HubDbContext.cs`, `Events/HubEvents.cs`, `Services/SyncIntakeService.cs`,
`Resilience/ReplicationClient.cs`, `Program.cs`, `.csproj` (hub) · `Directory.Packages.props` ·
`backend/docker-compose.yml`, `Caddyfile`, `.env.example` · both Postman representations ·
`IngestServiceTests.cs`, `TestHost.cs`, `HubTestHost.cs` · architecture overview, business rules,
`u10-components.md`

**`shared/` — untouched**, as D-U10-15 required.

---

## 4. Deviations from the plan, and why

| # | Deviation | Reason |
|---|---|---|
| 1 | **Step 22 signals from `SyncIntakeService` as well as `HubEventWriter`** | The plan named one file. But `HubEventWriter` only carries device-lifecycle events — spoke sync is where a tournament's actual event traffic arrives. Signalling only from the writer would have made "append-driven" true in name and timer-driven in fact. |
| 2 | **A global path-scoped limiter for the bulkhead, not a second endpoint policy** | An endpoint carries at most one `[EnableRateLimiting]`. The two layers protect different things, so the concurrency cap became a global limiter scoped to `/api/ingest`. |
| 3 | **`IHubCredentialReader` added** | Not in the plan. The singleton transport cannot hold the scoped credential store — the same captive-dependency hazard CL-1=A addresses for the client. |
| 4 | **The transport no longer mutates its `HttpClient`** | See §5 — found by the integration test. |

---

## 5. What the cross-solution test caught

F4=B was argued for on the grounds that a new authentication path deserves more than a human reading
a markdown file. It earned that on its first run, and not on the credential logic:

`HttpCloudReplicationTransport` was setting `BaseAddress`, `Timeout`, and a default header on a
client obtained from `IHttpClientFactory`. That works only because the factory hands out a fresh
`HttpClient` each call — the moment one is reused, `HttpClient` throws *"This instance has already
started one or more requests."* Nothing in the hub's own tests would have surfaced it, because they
stub the factory the same way production uses it.

Fixed by making the transport stateless with respect to the client: absolute URI and credential
header per `HttpRequestMessage`, and the timeout enforced with a linked `CancellationTokenSource`.
Configuring a client the transport does not own was the actual defect; the exception was the symptom.

Two smaller findings from the same test:
- Both `EventManager.Api` and `EventManager.Hub` declare a global `Program`, so referencing both makes the name ambiguous. First place in the repo that can happen (U10-CON-4). A controller type is used as the assembly marker instead.
- Both EF providers register in one container; the SQLite context needed its own internal service provider rather than unpicking Npgsql's registrations.

---

## 6. Corrections applied to approved artifacts

| # | Correction |
|---|---|
| **C-1** | `BR-REPL-3` said "salted hash". Corrected to SHA-256 unsalted: a salted hash cannot be looked up, so authentication would scan every row, and salting defeats rainbow tables against *low-entropy* secrets — a 256-bit random key has none. |
| **C-2** | `u10-components.md` claimed the hub library "stays platform-neutral". Too strong — the hub is a single project, so the DPAPI package lands in the same `.csproj`. Corrected to the accurate, narrower claim. |
| **C-3** | `ReplicationClient` kept a direct-store constructor alongside the scope-factory one, so `ResilienceTests.cs:56,117` compile and pass unchanged. |
| **BR-REPL-47** | Amended at NFR Design: the status route computes lag and pending together in one pass. |

---

## 7. Story coverage

US-801 ✅ · US-802 ✅ · US-803 ✅ · US-804 ✅ · US-805 ✅ · US-806 ✅ · US-807 ✅ · US-808 ✅ ·
US-809 ✅ · US-810 ✅ — every story has generation and test coverage. All 19 `U10-FR` implemented.

---

## 8. Not verified by execution

- **`otel-collector-config.yaml` and the `Caddyfile`** were written to their documented formats but **not run** — no Docker daemon was available during code generation. `docker compose config` parsed the compose file; the collector pipeline and the Caddy token gate are proven in the manual walkthrough (§3.6 of the verification guide).
- **No load test.** U10-NFR-1's 5-minute lag target is unit-tested for its *definition* (`BR-REPL-45`), not measured under load. Consistent with the project-level position that no performance pass has been run.
