# U10 HTTP Replication Adapter — Requirements Verification Questions

**Proposed unit**: U10 — Hub→Cloud HTTP Replication Adapter
**Proposed branch**: `unit/u10-http-replication`
**Stage**: INCEPTION → Requirements Analysis (Step 6 gate)

---

## What the code already gives us (verified, not assumed)

| Piece | Location | State |
|---|---|---|
| Batch driver | `admin/EventManager.Hub/Resilience/ReplicationClient.cs` | Exists. Computes the next batch per device high-water mark, retries, advances cursors from the ack. |
| Transport seam | `admin/EventManager.Hub/Resilience/CloudReplicationTransport.cs` | `ICloudReplicationTransport` exists; the **only** implementation is the in-process `StoreBackedReplicationTransport`. **This unit adds the HTTP one.** |
| Cloud endpoint | `backend/EventManager.Api/Controllers/EventIngestController.cs` | `POST /api/ingest/batch`, `[Authorize]`. |
| Cloud ingest logic | `backend/EventManager.Api/Services/IngestService.cs` | Idempotent, sequence-ordered, returns per-device high-water marks. |
| Wire contract | `shared/EventManager.Contracts/Dtos.cs` | `ReplicationBatchDto` / `ReplicationAckDto`. |

## What the code does **not** answer (why these questions exist)

1. **The hub has no cloud credential.** `IngestService.IngestAsync` authorizes the **caller account** — it requires `OrganizerAction.ManageRoster` on *every* `EventScopeId` in the batch. The cloud issues tokens only through `POST /api/accounts/login` (60-minute access token, 14-day rotating refresh token, TOTP-capable). Nothing in `admin/` holds or obtains one today.
2. **Retry is currently indiscriminate.** `ReplicationClient.SendWithRetryAsync` uses `catch when (attempt < maxAttempts)` — it retries *every* exception. Over HTTP that means a `401`/`403`/`400` is retried three times and then thrown out of `ReplicateAsync`.
3. **`IsOnline` has no real-network definition.** It is a settable `bool` on the in-process transport.
4. **Nothing triggers replication.** `admin/EventManager.Hub/Program.cs` registers `IReplicationProtocol`, `BackupService`, and `RecoveryService` — but never `ICloudReplicationTransport`, and no code path calls `ReplicateAsync`.
5. **Cursors are in-memory.** `_cloudHighWaterMarks` is a plain `Dictionary` field. A hub restart loses it, so the next run re-sends the entire log (idempotent and therefore *correct*, but O(whole log)).

Please answer each question by putting the letter after the `[Answer]:` tag. If none fit, choose the last option and describe.

---

## Question 1
How should the hub authenticate to the cloud ingest endpoint?

A) **Organizer sign-in at the hub.** At event setup an organizer signs in on the hub with their cloud email/password (+ TOTP if enrolled); the hub keeps the rotating refresh token and mints access tokens as needed. No backend change, MFA preserved. Cost: replication cannot start until a human signs in on that hub.

B) **Dedicated per-hub service account.** A Full Admin creates a normal cloud account, grants it organizer rights on the event, and its credentials are configured into the hub. Machine-friendly, but it is a non-human account that must not have MFA, and its password sits in hub configuration.

C) **New hub-registration credential in the backend.** Add a first-class concept — hub registers with the cloud and receives a long-lived, ingest-only API key or client credential. Cleanest security story (least privilege, no human password on the hub) but it is a real backend change: new entity, new endpoint, new authorization path, new migration.

D) **Extend the existing U4a device-pairing token** so the cloud accepts it as an ingest credential. Reuses a concept the system already has, but that token was designed for spoke→hub pairing on the LAN, not hub→cloud.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 2
Where should the hub store whatever credential Q1 selects?

**Context**: SQLCipher at-rest encryption is still a deferred seam (D-09), so `hub.db` is currently **plaintext SQLite**. SECURITY-12 ("no hardcoded credentials; use a secrets manager") is a *blocking* rule under the enabled Security Baseline extension, so option C below would be recorded as a blocking security finding unless you accept it explicitly.

A) **OS-protected secret store** — Windows DPAPI / .NET `ProtectedData` (the hub runs on Windows today). Encrypted at rest without waiting on SQLCipher.

B) **Operator-supplied configuration** — environment variable / .NET user-secrets / mounted secret file, never written to disk by the app. Simple, defers protection to the deployment.

C) **A row in `hub.db`**, plaintext until the SQLCipher seam lands. Simplest; explicitly accepts an at-rest exposure.

D) **Memory only** — the organizer re-authenticates every time the hub process starts. Nothing persisted at all; costs an interactive sign-in per restart.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 3
The HTTP adapter must distinguish "retry this" from "this will never succeed." Doing it properly means changing `ReplicationClient.SendWithRetryAsync` (U7 code, already merged). How far may this unit reach?

A) **Classify in the adapter and fix the client.** Adapter throws a distinct transient-vs-permanent exception type; `ReplicationClient` is amended to retry only transient ones. Correct behaviour, but modifies already-merged U7 code in `admin/`.

B) **Classify in the adapter only.** The adapter absorbs and retries transient failures internally and surfaces permanent ones as a single throw; `ReplicationClient` is left byte-for-byte untouched. Zero blast radius, but retry policy ends up split across two classes.

C) **Adapter classifies, and connectivity failures are reported as "offline" rather than thrown** — so a mid-event outage becomes a no-op that resumes on reconnect instead of an exception escaping `ReplicateAsync`. Also touches the client.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Question 4
How should `IsOnline` be determined against a real network?

A) **Optimistic** — always report online; just attempt the POST and let a connection failure be handled as an outage. No probe traffic.

B) **Health probe** — poll the cloud's health endpoint on a cadence and cache the answer.

C) **Circuit breaker** — start online; after N consecutive connection failures report offline and stop attempting until a cool-down elapses. (This is what RESILIENCY-10 asks for.)

D) **A + C** — optimistic first attempt, with a circuit breaker that opens after repeated connection failures.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 5
What triggers a replication run? (Nothing calls `ReplicateAsync` today.)

A) **Background timer** — a hosted service in the hub runs replication on an interval (e.g. every 30–60s) whenever there is a backlog.

B) **Manual only** — an operator-triggered hub endpoint (`POST /api/replication/run`) plus the existing completeness check, run at chosen points during/after the event.

C) **Both** — background timer for steady state, manual trigger for "push everything now, I'm about to tear down."

D) **Append-driven** — replicate shortly after each local event append (debounced).

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

## Question 6
`ReplicationClient._cloudHighWaterMarks` is in-memory, so a hub restart re-sends the whole log. Should this unit fix that?

A) **Persist cursors hub-locally** — store per-device cloud high-water marks in `hub.db`, reload on start. Hub-only change, no backend work.

B) **Ask the cloud on startup** — add a `GET /api/ingest/high-water-marks` endpoint and seed the cursors from the authoritative side. Most accurate (survives hub DB loss), but adds a backend endpoint + tests.

C) **Leave it** — re-sending is idempotent, so it is merely wasteful. Out of scope for this unit; log it as a known limitation.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Question 7
Transport security policy for the cloud base URL. (SECURITY-01 requires TLS in transit; the cloud sits behind Caddy per U3's infrastructure design.)

A) **HTTPS only, system trust** — reject any configured base URL that is not `https://`, validate with the OS certificate store.

B) **HTTPS enforced, with an explicit opt-out flag for local development** against `http://localhost` / the docker-compose stack.

C) **HTTPS + certificate pinning** to the cloud's certificate/public key — strongest, but pinning must be re-managed on every certificate rotation.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Question 8
What should the adapter expose operationally? (SECURITY-03 requires structured logging with no secrets; RESILIENCY-05 asks for health signals.)

A) **Structured logs only** — `ILogger` with correlation ids, matching the hub's current console logging. No new surface.

B) **Logs + replication status on the hub `/health` endpoint** — last successful replication time, pending backlog count, consecutive-failure count, circuit state. Gives the operator a "is the cloud current?" answer at the venue.

C) **Logs + status + a metrics exporter** (OpenTelemetry / Prometheus). Most observable; adds a dependency and something to scrape.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 9
What is the blast radius of this unit?

A) **Hub only** — new `HttpCloudReplicationTransport` in `admin/EventManager.Hub/Resilience/` plus composition-root wiring. `backend/` and `shared/` untouched.

B) **Hub + minimal backend** — as A, plus whatever Q1/Q6 imply (a credential endpoint and/or a high-water-mark query endpoint).

C) **Hub + backend hardening** — as B, plus hardening the ingest route itself: explicit request-body size limit and rate limiting on `POST /api/ingest/batch` (SECURITY-05 / SECURITY-11), which the endpoint does not have today.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 10
RESILIENCY-02 requires recovery targets. U3 already fixed cloud-side targets (criticality Medium, 99.5% availability, RTO 4h, RPO 24h). Does the hub→cloud replication path need its own objective?

A) **Inherit U3's targets unchanged** — replication is best-effort; the hub is the authority during the event and the cloud catches up whenever it can.

B) **Add a replication-lag objective** — e.g. "under normal connectivity the cloud is no more than N minutes behind the hub," with the lag surfaced and alerted on. (If you pick this, state N after the tag, or I will propose one.)

C) **Add a completeness deadline instead** — no continuous lag target, but 100% of the local log must be mirrored before the event is declared closed (this is US-602, already implemented as `VerifyCompletenessAsync`).

D) **B and C together.**

X) Other (please describe after [Answer]: tag below)

[Answer]: D - 5 mins

---

## Question 11
How should the adapter be tested? The Property-Based Testing extension is enabled and blocking, and this matters structurally: the hub tests live in `admin/EventManager.Admin.slnx` while the cloud API lives under `backend/`, so testing against the *real* controller means a cross-solution project reference that does not exist today.

A) **Mocked HTTP only** — a stub `HttpMessageHandler` inside the existing admin test project. Fast, no new references; verifies the adapter's own behaviour (serialization, auth header, classification, retry) but never the real endpoint.

B) **In-process against the real cloud API** — the admin test project references `backend/EventManager.Api` and drives a `WebApplicationFactory`, so hub and cloud are exercised end to end in one test run. Highest confidence, but couples the two solutions.

C) **A in the admin solution, plus B as a separate integration test project** that is allowed to reference both sides — keeps the unit tests clean and puts the coupling in one clearly-labelled place.

D) **A, plus a documented manual walkthrough** against the docker-compose stack in the end-of-unit testing guide (matching how U9's integration scenarios were handled).

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

## Not asked (decided, and stated so you can override)

- **Batch sizing** — the existing 500-envelope cap is kept and a byte-size cap is added so a batch of large payloads cannot exceed the server's request-body limit; the adapter splits rather than fails. Say so if you want different numbers.
- **Timeouts** — every HTTP call gets an explicit timeout (RESILIENCY-10 forbids unbounded waits). Default proposal: 30s per request.
- **Serialization** — `System.Text.Json` with the existing `ReplicationBatchDto`; no new wire contract, no `shared/` change.
- **No ternaries** — coding standard CS-1 applies to all new code.
