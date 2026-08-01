# U10 HTTP Replication Adapter — Code Generation Plan

**Stage**: CONSTRUCTION → Code Generation, Part 1 (Planning)
**Unit**: U10 · **Branch**: `unit/u10-http-replication` · **Workspace root**: `c:\repos\EventManager`
**Project type**: Brownfield — modify existing files in place; never create `*_new` or `*_modified` copies.

**This plan is the single source of truth for Code Generation.** Part 2 executes these steps in order
and marks each `[x]` as it completes.

---

## Unit context

| | |
|---|---|
| **Stories** | US-801..US-810 (Epic 8); amended US-504, US-602 |
| **Requirements** | U10-FR-1..19, U10-NFR-1..8 |
| **Rules** | BR-REPL-1..50, property P-REPL-1 |
| **Design** | AD-Q1..Q9 + CL-1/CL-2 · FD-Q1..Q8 + CL-A/CL-B · ND-Q1..Q9 · ID-Q1..Q6 · TS-U10-1..9 |
| **Dependencies** | U1 (`IEventStore`, `IReplicationProtocol`), U2 (`ReplicationBatchDto`, `EventEnvelopeMapper`), U3 (`IngestService`, `EventAuthorizer`, `AppDbContext`), U4a (`HubEventWriter`, `HubDbContext`, `OfflineOrganizerAuth`), U7 (`ReplicationClient`, `ICloudReplicationTransport`) |
| **Owned entities** | `HubCredential` (cloud), `HubCredentialRow` (hub), one nullable column on `EventRecord` |
| **Frozen** | `shared/` — no file in it is touched (D-U10-15) |

---

## Three corrections to approved artifacts, to apply during generation

Found while grounding this plan. Each is recorded here rather than silently worked around.

### C-1 · `BR-REPL-3` says "salted hash" — it should be an unsalted SHA-256

`RefreshTokenStore.Hash` (`Persistence/RefreshTokenStore.cs`) already establishes the repo's pattern:
`Convert.ToHexString(SHA256.HashData(...))`, unsalted, so a presented token can be found by hash in
one indexed lookup.

A *salted* hash cannot be looked up — authentication would have to scan every credential row and
verify each one. And salting exists to stop rainbow-table attacks on **low-entropy** secrets; a
256-bit random key (`BR-REPL-2`) has no rainbow table. So the salt would buy nothing and cost the
lookup.

**Applied**: `BR-REPL-3` is implemented as SHA-256 of the key, matching `RefreshTokenStore`. The rule
text will be amended to say so.

### C-2 · The hub is one project, so "the library stays platform-neutral" was too strong

`admin/EventManager.Hub` is a single `Microsoft.NET.Sdk.Web` project that is *both* library and host.
There is no separate host project for the DPAPI package reference to live in, so it lands in the same
`.csproj` as the library code — contrary to how `u10-components.md` phrased U10-CON-1's mitigation.

**What is still true, and is the accurate claim**: `System.Security.Cryptography.ProtectedData`
builds on every platform (it throws only at runtime, off Windows), so the project still compiles
anywhere. `ISecretProtector` keeps `HubCredentialStore` free of any Windows dependency, tests use a
pass-through implementation, and only the composition root names DPAPI. A future library/host split
becomes a project-file change rather than a refactor.

**Applied**: the U10-CON-1 mitigation note in `u10-components.md` will be corrected to this wording.

### C-3 · `ResilienceTests.cs` constructs `ReplicationClient` directly

Lines 56 and 117 do `new ReplicationClient(hub.Events, new ReplicationProtocol(), transport)`. AD-Q4=B
changes that constructor.

**Applied**: `ReplicationClient` keeps a constructor that accepts an `IEventStore` directly for
direct-drive use, alongside the scope-factory constructor used when hosted. Both call sites keep
working unchanged. If that proves impossible, the fallback is updating the two call sites — but the
17 admin tests must pass either way, and that is the gate.

---

## PART A — Cloud (`backend/EventManager.Api`)

- [x] **Step 1** — `Persistence/Entities.cs`: add `HubCredentialRecord` (CredentialId, EventScopeId, KeyHash, Label, IssuedByAccountId, IssuedAt, ExpiresAt, RevokedAt). Add nullable `IngestedByCredentialId` to `EventRecord`. *(E-1, E-2 · FD-Q7=B)*
- [x] **Step 2** — `Persistence/AppDbContext.cs`: `DbSet<HubCredentialRecord>`, index on `KeyHash`, index on `EventScopeId`.
- [x] **Step 3** — `Services/HubCredentialService.cs`: `IssueAsync` / `AuthenticateAsync` / `RevokeAsync` / `ListAsync`. Key = 256-bit CSPRNG, returned once; SHA-256 hash stored (C-1); expiry = `Event.Date` + grace; active-credential cap of 3. *(BR-REPL-1..8, 14, 15 · US-801, US-808)*
- [x] **Step 4** — `Auth/HubCredentialAuthenticationHandler.cs`: `"HubCredential"` scheme; generic failure carrying no detail. *(BR-REPL-7, 9 · US-809)*
- [x] **Step 5** — `Auth/IngestCaller.cs` + `CurrentCaller` resolver: account caller or hub-credential caller. *(E-3 · AD-Q3=A)*
- [x] **Step 6** — `Services/IngestService.cs` **(modify)**: take `IngestCaller`; hub callers authorize against their bound scope only, whole-batch refusal on any foreign scope; record provenance at insert; add `HighWaterMarksAsync`. *(BR-REPL-10, 13, 19..21 · U10-FR-3, 12)*
- [x] **Step 7** — `Controllers/HubCredentialController.cs`: POST / GET / DELETE under `/api/events/{eventId}/hub-credentials`, JWT scheme. *(US-801, US-808)*
- [x] **Step 8** — `Controllers/EventIngestController.cs` **(modify)**: accept both schemes, resolve `IngestCaller`, add `GET /api/ingest/high-water-marks`, apply the `"ingest"` rate-limit policy and body-size limit. *(U10-FR-3, 12, 15)*
- [x] **Step 9** — `Program.cs` **(modify)**: register the `"HubCredential"` scheme beside JWT; add the `"ingest"` rate-limit policy partitioned by **hash of the credential header** (limiter runs pre-auth), a global concurrency limiter, and an `OnRejected` handler emitting `Retry-After`. *(P-5, P-8, P-9 · ND-Q1/Q2/Q3)*
- [x] **Step 10** — EF migration `HubCredentials` in `Persistence/Migrations/` (third after `InitialCreate`, `AccountSoftDelete`). Additive only.
- [x] **Step 11** — `backend/tests/EventManager.Api.Tests/HubCredentialTests.cs`: issuance authorization, one-time key return, hash-only storage, cap of 3, expiry from event date, revocation immediacy, expired ≡ revoked, cross-event refusal, whole-batch refusal, provenance set once and first-writer-wins, non-disclosure of failure reason.

## PART B — Hub (`admin/EventManager.Hub`)

- [x] **Step 12** — `Resilience/ReplicationOptions.cs`: every knob from the NFR Design table with defaults, data-annotation ranges, and the cross-field rule (max batch bytes < server body limit). *(ND-Q8=A)*
- [x] **Step 13** — `Resilience/SecretProtection.cs`: `ISecretProtector`, `DpapiSecretProtector` (`CurrentUser`), `PassthroughSecretProtector`. *(ND-Q5=A · C-2)*
- [x] **Step 14** — `Persistence/HubEntities.cs` + `HubDbContext.cs` **(modify)**: `HubCredentialRow`. New `Resilience/HubCredentialStore.cs` — install refuses when occupied, explicit clear, never returns the key. *(BR-REPL-22..25 · FD-Q8=B)*
- [x] **Step 15** — `Resilience/ReplicationFailureClassifier.cs`: pure function, four failure kinds, `Retry-After` extraction. *(BR-REPL-29..32)*
- [x] **Step 16** — `Resilience/ReplicationCircuitBreaker.cs`: 3 failures / 60s / single trial; connection failures only. *(BR-REPL-34..36)*
- [x] **Step 17** — `Resilience/ReplicationSignal.cs`: bounded channel, non-blocking, drop-on-full. *(BR-REPL-37)*
- [x] **Step 18** — `Resilience/ReplicationStatus.cs`: cached snapshot for `/health` and metrics; on-demand computation path for the status route. *(BR-REPL-45..48 · ND-Q6=C)*
- [x] **Step 19** — `Resilience/ReplicationMetrics.cs`: `Meter` `eventmanager.replication.*`, six instruments, no credential in any tag. *(P-14 · ND-Q7=A)*
- [x] **Step 20** — `Resilience/HttpCloudReplicationTransport.cs`: `ICloudReplicationTransport` over `IHttpClientFactory`; credential header; HTTPS enforcement with dev override; 30s timeout; `GetHighWaterMarksAsync`. *(U10-FR-1, 12, 14 · BR-REPL-26, 27)*
- [x] **Step 21** — `Resilience/ReplicationClient.cs` **(modify — the only merged-U7 edit)**: retry only transient failures, honour `Retry-After`; `BackgroundService` loop consuming signal / drain timer; `SeedCursorsAsync`; `FlushForCloseOutAsync` bounded to the configured window; `IServiceScopeFactory` per run (CL-1=A); direct-store constructor retained (C-3). *(BR-REPL-33, 38..44)*
- [x] **Step 22** — `Events/HubEventWriter.cs` **(modify)**: one `ReplicationSignal.Signal()` after a successful append. *(AD-Q5=C — the only U4a change)*
- [x] **Step 23** — `Controllers/HubControllers.cs` **(modify)** or new `ReplicationController`: `POST`/`DELETE /api/replication/credential`, `GET /api/replication/status`, `POST /api/replication/close-out`, behind `OfflineOrganizerAuth`. *(US-802, US-806, US-807)*
- [x] **Step 24** — `Program.cs` **(modify)**: register options with validate-on-start, protector, store, classifier, breaker, signal, status, metrics, named `HttpClient`, transport, hosted `ReplicationClient`; OTel meter provider + OTLP exporter; extend `/health` with replication status. *(TS-U10-1, 4, 7)*
- [x] **Step 25** — `admin/tests/EventManager.Hub.Tests/ReplicationAdapterTests.cs`: stub `HttpMessageHandler` covering classification, retry-only-transient, `Retry-After`, breaker open/cool-down/close, HTTPS refusal, batch splitting, cursor seeding incl. unreachable-cloud start, close-out bound, credential install refusal when occupied, DPAPI round-trip via pass-through, no-credential no-op, non-blocking signal under a full channel. Plus **`P-REPL-1`** as an FsCheck property. *(PBT-01)*

## PART C — Cross-solution integration test

- [x] **Step 26** — Create `tests/EventManager.Integration.Tests/` referencing **both** `admin/EventManager.Hub` and `backend/EventManager.Api`, plus `EventManager.Integration.slnx` at the repo root. *(CL-2=A · U10-CON-4)*
- [x] **Step 27** — `CredentialPathTests.cs`: real adapter → real `EventIngestController` via `WebApplicationFactory`. Valid scoped credential succeeds; revoked, expired, and wrong-event credentials are refused and **not retried**.

## PART D — Configuration and infrastructure

- [x] **Step 28** — `Directory.Packages.props`: `OpenTelemetry.Extensions.Hosting` 1.17.0, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.17.0, `System.Security.Cryptography.ProtectedData` 10.0.10. Reference all three from `admin/EventManager.Hub/EventManager.Hub.csproj` (C-2).
- [x] **Step 29** — `backend/docker-compose.yml` + new `backend/otel-collector-config.yaml`: collector `0.157.0`, `expose` only, `memory_limiter` first. *(ID-Q1/Q4/Q5)*
- [x] **Step 30** — `backend/Caddyfile` **(modify)**: JSON access logging for the whole site (**closes the pre-existing SECURITY-02 gap**), `/otlp/*` route with bearer-token gate. *(ID-Q2/Q3)*
- [x] **Step 31** — `backend/.env.example` **(modify)**: `METRICS_TOKEN` placeholder, no default.

## PART E — Documentation and deliverables

- [x] **Step 32** — Postman: add U10 requests to **both** representations — `postman/EventManager.postman_collection.json` and the directory format under `postman/collections/…/`. Regenerate the directory format from the JSON so the two cannot drift; assert absent headers with `pm.response.headers.has()`.
- [x] **Step 33** — `aidlc-docs/construction/u10-http-replication/code/code-summary.md`: created vs modified files, story coverage, extension compliance.
- [x] **Step 34** — Apply corrections C-1 (`business-rules.md`) and C-2 (`u10-components.md`).
- [x] **Step 35** — **End-of-unit deliverable**: update `inception/application-design/architecture-overview.md` to as-built with a U10 section and diagram.
- [x] **Step 36** — **End-of-unit deliverable**: `construction/u10-http-replication/code/user-testing-guide.md` — the manual docker-compose walkthrough that Q11=D makes this unit's primary integration verification, covering scenarios R-1..R-6.

---

## Story traceability

| Story | Steps |
|---|---|
| US-801 Issue a credential | 1, 2, 3, 7, 11 |
| US-802 Install on the hub | 13, 14, 23, 25 |
| US-803 Replicate during the event | 17, 20, 21, 22, 25 |
| US-804 Outage costs nothing | 15, 16, 21, 25 |
| US-805 Restart doesn't re-send | 6, 8, 20, 21, 25 |
| US-806 See whether the cloud is current | 18, 19, 23, 24, 29, 30 |
| US-807 Close out fully mirrored | 21, 23, 25 |
| US-808 Revoke | 3, 7, 11, 27 |
| US-809 Cannot be misused | 4, 5, 6, 9, 11, 20, 27 |
| US-810 Ingest survives abuse | 8, 9, 11 |

Every story has at least one generation step and at least one test step.

---

## Scope and sequencing

**36 steps.** Order follows the approved package sequence: cloud first (the credential's shape defines
the wire), backend tests, then hub, then the `ReplicationClient` edit isolated as its own step, then
hub tests, then the cross-solution test, then configuration and documentation.

**Quality gates carried into Part 2**
- All six solutions build with **0 warnings**.
- **153-test baseline does not regress**; the 17 existing admin tests stay green across Step 21.
- CS-1 — **no ternary operators** in new code.
- No file under `shared/` is modified.
- No duplicate or `_modified` files.
