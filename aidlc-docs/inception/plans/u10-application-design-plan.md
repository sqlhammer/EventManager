# U10 HTTP Replication Adapter — Application Design Plan

**Stage**: INCEPTION → Application Design (Part 1: plan + questions)
**Branch**: `unit/u10-http-replication` (created 2026-07-27, commit `f840324`)
**Inputs**: approved requirements (D-U10-01..15, U10-FR-1..19, U10-CON-1..6), approved Epic 8 (US-801..810), approved execution plan
**Artifact naming**: all outputs are `u10-`-prefixed so the MVP design documents in `inception/application-design/` are not overwritten.

---

## Scope of this stage

Component boundaries, interfaces, method signatures, and dependencies — **not** business rules, which belong to Functional Design. The decisive item is **U10-CON-5**: how a credential travels from the cloud to a hub that has no UI. That is a component-interaction decision and this stage owns it.

### Grounding checked before writing these questions

| Fact | Where | Consequence |
|---|---|---|
| The API already uses ASP.NET Core's built-in rate limiter with named policies (`login`, `registration`) | `backend/EventManager.Api/Program.cs:106-114` | U10-FR-15 is a **new named policy**, not a new dependency. This shrinks the NFR Requirements stage — one of its three tech-stack questions is already answered by existing code. |
| Authentication is a single JWT bearer scheme | `Program.cs:89` | A second principal type has to be added deliberately (AD-Q2). |
| The hub already hosts authenticated controllers (`api/pairing`, `api/sync`, `api/events/{id}/devices`) with `OfflineOrganizerAuth` | `admin/EventManager.Hub/Controllers/HubControllers.cs` | A credential-install endpoint on the hub is a small addition to an existing surface, not a new concept (AD-Q1). |
| `IngestService` authorizes `callerAccountId` against `OrganizerAction.ManageRoster` | `backend/EventManager.Api/Services/IngestService.cs:24` | A hub credential is not an account, so this authorization path must change (AD-Q3). |

---

## PART 1a — Design Questions

Answer each by putting the letter after the `[Answer]:` tag.

---

### AD-Q1 — How does a credential get from the cloud onto the hub? (U10-CON-5)

The blocking gap carried from requirements. The hub has no UI; the MAUI shell is still a deferred seam.

A) **Hub admin endpoint** — `POST /api/replication/credential` on the hub's existing ASP.NET Core surface, protected by the hub's existing `OfflineOrganizerAuth`. The organizer pastes the key (Postman/curl today, a MAUI screen later). *Smallest new concept — the hub already has an authenticated organizer surface and controllers.* **My lean.**

B) **First-run configuration bootstrap** — the key arrives as an environment variable / secret file, and the hub imports it into protected storage at startup. No new endpoint; fits headless deployment; awkward to rotate without a restart.

C) **Hub-initiated enrolment** — the organizer hands the hub a short-lived cloud token; the hub calls the cloud itself and receives the long-lived credential, which therefore never passes through a human's clipboard. *Best security story*, but it adds a second cloud flow and a second credential type.

D) **A and B** — endpoint for normal use, configuration for headless or automated setup.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q2 — How does the cloud authenticate a hub credential?

A) **A second authentication scheme** — a custom `AuthenticationHandler` registered alongside JWT bearer; ingest routes declare `[Authorize(AuthenticationSchemes = "HubCredential")]`. Idiomatic ASP.NET Core; the two principal types can never be confused because the route says which it accepts. **My lean.**

B) **Middleware or an action filter** on the ingest routes that validates the credential and populates a caller context, outside the authentication pipeline.

C) **Reuse JWT** — the "credential" is just a long-lived JWT, so nothing changes in the auth pipeline. *Note before choosing this*: its simplicity is partly illusory. A JWT cannot be revoked without a server-side revocation list, and US-808 requires revocation to take effect immediately — so the lookup C is trying to avoid has to be built anyway.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q3 — What does a hub credential mean to `IngestService`?

Today `IngestService.IngestAsync(long callerAccountId, ...)` requires the caller to hold `ManageRoster` on every scope in the batch.

A) **A distinct principal** — ingest learns about a caller that may be an account *or* a hub credential, and a hub credential authorizes by its own bound event scope. Account-based ingest keeps working. **My lean.**

B) **Map to the issuing account** — the credential resolves to the organizer's account id and `IngestService` is unchanged. *Trade-off worth naming*: cloud-side audit would then attribute every hub write to a person who was not present, and the credential's permissions would silently track that organizer's role changes rather than its own scope.

C) **Replace** — only hub credentials may ingest; account-based ingest is removed. Cleanest end state, but it breaks existing tests and the Postman collection.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q4 — Where does replication triggering live?

Three triggers are required (D-U10-05): debounced append, drain timer, close-out flush.

A) **A `ReplicationScheduler` hosted service** in `admin/EventManager.Hub` owning all three, with `ReplicationClient` left as a pure batch driver. *Keeps the edit to merged U7 code minimal — the only `ReplicationClient` change stays the retry-classification fix.* **My lean.**

B) **Inside `ReplicationClient`** — one class owns driving and scheduling. Fewer moving parts; a larger change to merged U7 code.

C) **In the host `Program.cs`** — timers wired directly at composition. Least structure; hardest to test.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### AD-Q5 — How does the append-driven trigger learn that an append happened?

A) **The writer notifies** — `HubEventWriter` raises an in-process notification the scheduler subscribes to. Genuinely append-driven; touches U4a's writer.

B) **The scheduler polls** the local high-water mark on a short interval. Touches nothing, but "append-driven" then really means "poll-driven", which is not what Q5=D chose.

C) **A channel** — the writer posts to an in-process channel the scheduler consumes; gives natural debouncing and back-pressure. Also touches the writer. **My lean.**

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

### AD-Q6 — Shape of the secret-protection seam (U10-CON-1)

A) **`ISecretProtector`** with a DPAPI implementation registered by the host and a pass-through implementation for tests. The hub library stays platform-neutral; only the composition root knows about Windows. **My lean.**

B) **Call `ProtectedData` directly** in the credential store. Fewer types; puts a Windows API in the library and makes the store untestable off Windows.

C) **`ISecretProtector` plus a cross-platform AES fallback** keyed from a file. *Note*: this re-opens the key-management problem that D-09 deliberately deferred — where does the file's key live?

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q7 — Where does replication status for `/health` come from?

A) **A `ReplicationStatus` singleton** updated by the scheduler and transport; `/health` reads it. Cheap, always available offline. **My lean.**

B) **Computed on demand** from the event store and cursors at each health request. Always accurate, costs a query per probe.

C) **Both** — live counters plus an on-demand backlog computation.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q8 — How is the hub instrumented for metrics?

A) **`System.Diagnostics.Metrics.Meter`** in hub components, exported by OpenTelemetry wired at the composition root. No OTel types in component code; the exporter is swappable. **My lean.**

B) **OpenTelemetry API directly** in the components.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### AD-Q9 — Where does the one cross-solution test live? (U10-CON-4)

F4=B asked for one narrow end-to-end test of the credential path, and one clearly-labelled place for the coupling.

A) **In `admin/tests/EventManager.Hub.Tests`**, with a project reference to `backend/EventManager.Api`. The adapter under test is the hub's, so the test sits with its subject.

B) **In `backend/tests/EventManager.Api.Tests`**, referencing `admin/EventManager.Hub`. The backend test project already has the `TestHost`/`WebApplicationFactory` pattern this test needs, so less new scaffolding. **My lean.**

C) **A new `tests/EventManager.Integration.Tests` project** at the repo root referencing both. Cleanest isolation of the coupling; adds a sixth test project and a solution question.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## PART 1b — Resolved Decisions

| Question | Answer | Resolution |
|---|---|---|
| AD-Q1 Credential delivery (U10-CON-5) | **A** | Hub admin endpoint `POST /api/replication/credential`, behind the hub's existing `OfflineOrganizerAuth`. **This closes U10-CON-5**, carried open since Requirements Analysis. |
| AD-Q2 Cloud authentication mechanism | **A** | A second authentication scheme, `"HubCredential"`, registered alongside JWT bearer. Routes declare which schemes they accept. |
| AD-Q3 Ingest principal model | **A** | `IngestService` takes an `IngestCaller` that is either an account or a hub credential; a hub credential authorizes by its own bound event scope. Account-based ingest keeps working. |
| AD-Q4 Scheduler placement | **B** | `ReplicationClient` owns driving **and** scheduling. Widens the merged-U7 edit — see the U10-CON-6 amendment. |
| AD-Q5 Append notification | **C** | `HubEventWriter` posts to an in-process channel that `ReplicationClient` consumes, giving debouncing and back-pressure. |
| AD-Q6 Secret protection seam | **A** | `ISecretProtector`, with `DpapiSecretProtector` registered by the host and a pass-through for tests. Library stays platform-neutral. |
| AD-Q7 Health status source | **A** | A `ReplicationStatus` singleton updated by the client and transport; `/health` reads it, so status survives an outage. |
| AD-Q8 Instrumentation | **A** | `System.Diagnostics.Metrics.Meter` in components; OpenTelemetry export wired at the composition root only. |
| AD-Q9 Cross-solution test location | **C** | New `tests/EventManager.Integration.Tests` at the repo root. |
| **CL-1** Store access for the long-lived client | **A** | `IServiceScopeFactory`, one scope per replication run. Avoids a singleton capturing the scoped, non-thread-safe `HubDbContext`. |
| **CL-2** Solution ownership of the new test project | **A** | A sixth solution, `EventManager.Integration.slnx`, so the credential-path test actually runs in the verification sweep. Build-and-Test gains a sixth `dotnet test` line. |

**Corrections applied as a consequence of AD-Q4=B** (flagged at the clarification round, not silently):
U10-CON-6 in the approved requirements and §4 step 5 of the approved execution plan both described the
`ReplicationClient` edit as limited to retry classification. Both have been amended to state the real
scope. No other approved statement changed.

---

## PART 2 — Execution Checklist

*(Executed only after explicit plan approval.)*

### Preparation
- [x] Re-read the approved requirements, Epic 8, and the execution plan's package sequence
- [x] Re-read the existing MVP design artifacts (`components.md`, `services.md`, `component-dependency.md`) to match house style and avoid contradicting established boundaries
- [x] Confirm no existing component's stated responsibility is silently changed by this unit

### Mandatory artifacts
- [x] Generate `u10-components.md` — component definitions, responsibilities, interfaces
- [x] Generate `u10-component-methods.md` — method signatures with input/output types (no business rules; those go to Functional Design)
- [x] Generate `u10-services.md` — service definitions and orchestration patterns
- [x] Generate `u10-component-dependency.md` — dependency matrix, communication patterns, data flow
- [x] Generate `u10-application-design.md` consolidating the above
- [x] Validate design completeness and consistency

### Design content
- [x] Resolve U10-CON-5 explicitly per AD-Q1 and record it as closed
- [x] Define the cloud-side credential components per AD-Q2/AD-Q3
- [x] Define the hub-side components: credential store, secret protector, HTTP transport, scheduler, status
- [x] Specify the exact change to `ReplicationClient` and confirm it stays limited to retry classification
- [x] Record which existing components are touched and which are explicitly not
- [x] Map every component to the U10-FR and US-8xx it serves

### Verification
- [x] Every U10-FR (1–19) has an owning component
- [x] No component boundary contradicts an approved decision D-U10-01..15
- [x] U10-CON-1, -3, -4, -5, -6 each addressed or explicitly carried forward with a named owner stage
- [x] Extension applicability assessed for this stage (SECURITY-06/08/11/12, RESILIENCY-10) and recorded

### Completion
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting
- [x] Mark every checklist item [x] in the same interaction as the work
