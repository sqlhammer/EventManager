# Requirements — Unit U10: HTTP Replication Adapter

**Unit**: U10 — Hub→Cloud HTTP Replication Adapter
**Branch**: `unit/u10-http-replication` (per the per-unit git branch process requirement)
**Stage**: INCEPTION → Requirements Analysis
**Depth**: Standard, with comprehensive traceability (matches U9)
**Source answers**: `u10-http-replication-verification-questions.md` (Q1–Q11) and `u10-http-replication-clarification-questions.md` (F1–F4)

---

## 1. Intent Analysis

| Dimension | Assessment |
|---|---|
| **User request** | "HTTP replication adapter — highest value. Unblocks both integration scenarios, and both ends already exist." |
| **Request clarity** | Clear |
| **Request type** | New Feature — implements a deliberately deferred U7 seam |
| **Scope estimate** | **Multiple Components** — `admin/EventManager.Hub`, `backend/EventManager.Api`, and the cloud Compose stack. `shared/` is **not** touched. |
| **Complexity estimate** | **Complex** |

### Scope grew during requirements — stated plainly

The request assumed this unit "needs no decision from you." It did. The answers turned a hub-only
adapter into a three-surface change:

- **Q1=C** adds a first-class hub credential to the backend — a new entity, a new issuing endpoint, and a **new authentication path** alongside the existing account JWT.
- **Q6=B** adds `GET /api/ingest/high-water-marks`.
- **Q9=C** adds rate limiting and a request-body cap to the ingest route.
- **F3=B** adds an OTLP collector service to the cloud Compose stack — an infrastructure change.
- **Q3=A** modifies already-merged U7 code (`ReplicationClient`).
- **F4=B** creates the first project reference between the `admin/` and `backend/` solutions.

This is a legitimate set of choices, but it is not the "wire up the seam" unit the request described.
Workflow Planning should size it accordingly — Infrastructure Design is no longer skippable.

### Baseline: what exists today (verified by code inspection)

| Piece | Location | State |
|---|---|---|
| Batch driver | `admin/EventManager.Hub/Resilience/ReplicationClient.cs` | Computes batches above each device's cloud high-water mark; retries; advances cursors from the ack. Cursors are an **in-memory** `Dictionary`. |
| Transport seam | `admin/EventManager.Hub/Resilience/CloudReplicationTransport.cs` | `ICloudReplicationTransport` (`IsOnline` + `SendAsync`). Only implementation is the in-process `StoreBackedReplicationTransport`. |
| Cloud ingest endpoint | `backend/EventManager.Api/Controllers/EventIngestController.cs` | `POST /api/ingest/batch`, `[Authorize]`. No body-size cap, no rate limit. |
| Cloud ingest logic | `backend/EventManager.Api/Services/IngestService.cs` | Idempotent, sequence-ordered, folds projections, returns per-device high-water marks. Authorizes the **caller account** for `OrganizerAction.ManageRoster` on every scope in the batch. |
| Wire contract | `shared/EventManager.Contracts/Dtos.cs` | `ReplicationBatchDto` / `ReplicationAckDto` — **unchanged by this unit**. |
| Token issuance | `backend/EventManager.Api/Services/TokenService.cs` | Account JWT: 60-min access, 14-day rotating refresh, TOTP-capable. **Not** used by this unit. |
| Composition root | `admin/EventManager.Hub/Program.cs` | Never registers `ICloudReplicationTransport`; nothing calls `ReplicateAsync`. |

---

## 2. Decision Record (traceability)

| ID | Decision | Source |
|---|---|---|
| **D-U10-01** | The hub authenticates with a **backend-issued hub credential** — a new first-class concept. Not an organizer login, not the U4a device-pairing token. | Q1=C |
| **D-U10-02** | The credential is persisted in `hub.db`, with its value **DPAPI-protected** before write. A copied `hub.db` is useless on another machine. | Q2=C + F1=B |
| **D-U10-03** | The adapter **classifies** transient vs permanent failures, and `ReplicationClient.SendWithRetryAsync` is amended to retry only transient ones. | Q3=A |
| **D-U10-04** | `IsOnline` is driven by a **circuit breaker**: opens after N consecutive connection failures, closes after a cool-down. Defaults **N=3, cool-down 60s**, both configurable. | Q4=C |
| **D-U10-05** | Replication is triggered three ways: **append-driven (debounced)**, a **drain timer**, and an explicit **close-out flush**. | Q5=D + F2=C |
| **D-U10-06** | The hub seeds its cursors from the cloud at startup via a new **`GET /api/ingest/high-water-marks`**. | Q6=B |
| **D-U10-07** | **HTTPS is enforced** on the configured base URL, with an explicit opt-out flag for local development against `http://localhost`. | Q7=B |
| **D-U10-08** | Observability is logs + hub health status + **OTLP metrics exported to a new collector service** added to the cloud Compose stack. | Q8=C + F3=B |
| **D-U10-09** | Blast radius is **hub + backend + backend hardening**: a body-size cap and rate limiting are added to the ingest route. | Q9=C |
| **D-U10-10** | Two resiliency objectives: a **5-minute replication-lag target** under normal connectivity, and a **100%-mirrored completeness gate** before an event is declared closed. | Q10=D, "5 mins" |
| **D-U10-11** | Testing is stub-`HttpMessageHandler` unit tests + a manual docker-compose walkthrough + **one narrow in-process end-to-end test of the credential path**. | Q11=D + F4=B |
| **D-U10-12** | The hub credential's scope covers **both** batch write and cursor read — an "ingest-only" credential would be locked out of the D-U10-06 endpoint. | Deterministic consequence, flagged and not objected to |
| **D-U10-13** | `429 Too Many Requests` is classified **transient** and honours `Retry-After` — otherwise the hub throws on the rate limit that D-U10-09 points at it. | Deterministic consequence, flagged and not objected to |
| **D-U10-14** | Batching keeps the existing 500-envelope cap and adds a **byte-size cap** below the server's body limit; the adapter splits rather than fails. Every HTTP call carries an explicit **30s timeout**. | Stated in the questions file, not objected to |
| **D-U10-15** | `System.Text.Json` over the existing `ReplicationBatchDto`. No new wire contract, **no `shared/` change**. CS-1 (no ternaries) applies to all new code. | Stated in the questions file, not objected to |

---

## 3. The Hub Credential (core of this unit)

The single largest piece of new design. Today the cloud only knows how to authenticate a **person**;
this unit teaches it to authenticate a **hub**.

| Property | Requirement |
|---|---|
| **Issued by** | The cloud, to an authenticated organizer who holds rights on the event |
| **Bound to** | One event scope (`EventScopeId`) |
| **Permits** | `POST /api/ingest/batch` and `GET /api/ingest/high-water-marks` for that scope, and nothing else (D-U10-12) |
| **Lifetime** | Long-lived with an explicit expiry (Q1=C) |
| **Revocation** | Revocable from the cloud at any time; a revoked credential fails permanently and is never retried |
| **Cloud storage** | **Hash only** — the cloud never persists a usable key |
| **Hub storage** | One row in `hub.db`, value DPAPI-protected (D-U10-02) |
| **Never** | Appears in a log line, an error message, a metric label, or a health response |

**Least privilege (SECURITY-06)**: the credential cannot read event data, cannot register accounts,
cannot manage roster, and cannot ingest for any other event — it is strictly narrower than the
organizer account that issues it. Today's `IngestService` grants ingest to any account holding
`ManageRoster`; the hub credential is a separate, narrower principal.

---

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| **U10-FR-1** | `HttpCloudReplicationTransport` implements `ICloudReplicationTransport`, POSTing `ReplicationBatchDto` to `{baseUrl}/api/ingest/batch` and returning the deserialized `ReplicationAckDto`. |
| **U10-FR-2** | The cloud issues hub credentials per §3, to an authenticated organizer with rights on the target event, storing only a hash. |
| **U10-FR-3** | The cloud authenticates hub credentials on the ingest routes and authorizes them **per event scope** — a credential for event A is refused for event B. |
| **U10-FR-4** | A revoked or expired credential is refused, and the refusal is classified **permanent** — never retried. |
| **U10-FR-5** | The hub persists its credential DPAPI-protected in `hub.db` and loads it at startup. Provisioning path is per U10-CON-5. |
| **U10-FR-6** | The adapter classifies every failure: **transient** = connection failure, timeout, `408`, `429`, `5xx`; **permanent** = `400`, `401`, `403`, `404`, `413`, `422`, and deserialization failures. |
| **U10-FR-7** | `ReplicationClient.SendWithRetryAsync` retries only transient failures; a permanent failure propagates immediately without consuming retry attempts. |
| **U10-FR-8** | `429` responses honour `Retry-After` when present, falling back to the existing exponential backoff. |
| **U10-FR-9** | `IsOnline` reflects a circuit breaker: closed initially; opens after 3 consecutive **connection** failures; a cool-down of 60s then permits a trial request. Opening is a no-op for replication, not an error. |
| **U10-FR-10** | Replication is triggered by (a) a local append, debounced; (b) a drain timer that fires when a backlog exists or the breaker cool-down has elapsed; (c) an explicit close-out flush. |
| **U10-FR-11** | The close-out flush replicates to completion and then runs `VerifyCompletenessAsync`, reporting whether 100% of the local log is mirrored. |
| **U10-FR-12** | `GET /api/ingest/high-water-marks` returns per-device cloud high-water marks for the credential's event scope; the hub seeds `ReplicationClient`'s cursors from it at startup. |
| **U10-FR-13** | Batches are capped at 500 envelopes **and** a configured byte size; oversized batches are split, never dropped or failed. |
| **U10-FR-14** | A non-HTTPS base URL is rejected at startup unless the explicit development opt-out flag is set. |
| **U10-FR-15** | `POST /api/ingest/batch` enforces an explicit request-body size limit and a rate limit, both configured above the hub's expected burst rate. |
| **U10-FR-16** | The hub emits structured logs for every replication attempt (outcome, batch size, duration, correlation id) with **no credential material**. |
| **U10-FR-17** | The hub `/health` endpoint reports replication status: last successful replication time, pending backlog, consecutive failures, and circuit state. |
| **U10-FR-18** | The hub exports OTLP metrics to a collector service added to the cloud Compose stack. |
| **U10-FR-19** | Replication remains **idempotent and gap-free** end to end: a re-sent batch adds nothing, and an outage resumes without a gap. |

---

## 5. Non-Functional Requirements

| ID | Requirement | Source |
|---|---|---|
| **U10-NFR-1** | Under normal connectivity the cloud is **no more than 5 minutes** behind the hub. | D-U10-10 |
| **U10-NFR-2** | 100% of the local event log is mirrored before an event is declared closed (US-602). | D-U10-10 |
| **U10-NFR-3** | Every HTTP call has an explicit **30s timeout** — no unbounded waits. | RESILIENCY-10 |
| **U10-NFR-4** | An outage is a **no-op**, never data loss: the hub keeps operating and resumes gap-free (NFR-1.1, NFR-1.2). | U7 inherited |
| **U10-NFR-5** | No credential material in logs, metrics, health responses, or error messages. | SECURITY-03 |
| **U10-NFR-6** | All hub→cloud traffic is TLS 1.2+ except under the explicit development opt-out. | SECURITY-01 |
| **U10-NFR-7** | Cloud availability/RTO/RPO targets are inherited unchanged from U3 (Medium criticality, 99.5%, RTO 4h, RPO 24h). This unit adds no new cloud workload. | U3-NFR-R1/R2 |
| **U10-NFR-8** | Replication runs in the background without degrading hub responsiveness during an event (NFR-5.1: 300 athletes, ~8 mats, ~20 devices). | U3/U4a inherited |

---

## 6. Constraints and Open Design Decisions

### U10-CON-1 — DPAPI makes the hub Windows-only in code, not just in practice
`admin/EventManager.Hub` is currently platform-neutral C# — it is Windows-only today because of the
MAUI toolchain, not because of its source. DPAPI (`System.Security.Cryptography.ProtectedData`) is a
Windows API, so D-U10-02 puts a platform dependency in the hub library for the first time.
**Mitigation to decide at Functional Design**: introduce an `ISecretProtector` seam with a DPAPI
implementation, so a future non-Windows hub is an added implementation rather than a refactor.

### U10-CON-2 — The metrics collector is blind exactly when it matters
F3=B sends metrics to a collector in the **cloud** stack. During an outage — the scenario the whole
unit exists to survive — the hub cannot reach it. Metrics buffer in memory and are lost on restart.
This is an accepted consequence of choosing B over C, recorded so nobody later mistakes the
dashboard's silence for the hub being healthy. Local `/metrics` (option A) remains available as a
future addition.

### U10-CON-3 — The rate limit points at our own hub
D-U10-09 rate-limits an endpoint whose only production caller is our own hub. Set too low, normal
replication self-throttles into `429`s. **Design must pick a concrete limit** derived from the
expected burst — 500-envelope batches drained after an outage — with headroom, and D-U10-13's
`Retry-After` handling is what keeps a mis-set limit from becoming an incident.

### U10-CON-4 — First coupling between the `admin/` and `backend/` solutions
F4=B requires the admin test project to reference `backend/EventManager.Api`. No such reference
exists today; the five solutions have been independent. Design should confine it to a single,
clearly-named test file so the coupling stays visible and does not spread into production code.

### U10-CON-5 — Credential provisioning has no delivery path yet
The cloud can *issue* a hub credential (U10-FR-2) and the hub can *store* one (U10-FR-5), but
nothing connects them: the hub's MAUI UI is still a deferred seam, so there is no screen for an
organizer to paste a key into. **Functional Design must choose**: a hub admin endpoint that accepts
the credential, a configuration-file bootstrap on first run, or a hub-initiated enrolment using a
short-lived organizer token. This is the U10 analogue of U9-CON-1 — a real gap surfaced at
requirements rather than discovered during code generation.

### U10-CON-6 — This unit modifies merged U7 code
D-U10-03 changes `ReplicationClient`, merged at `0b51346`. The 17 existing admin tests must stay
green, and the change must not alter in-process `StoreBackedReplicationTransport` behaviour.

**Amended 2026-07-27 (Application Design AD-Q4=B).** This constraint was written assuming the edit
would be limited to retry classification. It is not. `ReplicationClient` now also owns the replication
schedule: a channel consumer for append signals, a drain timer, the close-out flush, and a
`BackgroundService` lifetime. Two consequences follow:

- The class becomes long-lived, while `IEventStore`/`HubEventStore` and `HubDbContext` are registered
  **scoped** (`admin/EventManager.Hub/Program.cs:16,40`). Per CL-1=A it takes `IServiceScopeFactory`
  and creates a scope per replication run — a singleton holding a scoped, non-thread-safe `DbContext`
  would fail intermittently under concurrency rather than at startup.
- The "17 admin tests stay green" gate is now the primary protection for the flagship offline
  guarantee across a substantially larger edit, not a formality. `ReplicationClient` is currently
  constructed directly in `ResilienceTests.cs:56,117` and is not registered in DI, so those tests
  must keep working against the amended constructor.

---

## 7. Out of Scope

- SQLCipher at-rest encryption for `hub.db` (D-09 remains deferred; DPAPI protects the credential specifically, not the database).
- Non-Windows secret protection (see U10-CON-1).
- Hot standby / second hub.
- Spoke→hub transport, mDNS, and SignalR — still no-op seams.
- The hub MAUI UI shell.
- The Blazor web portal (untracked input-document change; no unit exists).
- Dashboards and alert rules on the new metrics — this unit **emits** metrics; consuming them is an Operations concern.
- mTLS / certificate pinning (Q7=B chose system trust).
- Changing `ReplicationBatchDto` / `ReplicationAckDto` or anything else in `shared/`.

---

## 8. Extension Compliance (Requirements stage)

### Security Baseline — all rules blocking

| Rule | Status | Note |
|---|---|---|
| SECURITY-01 Encryption at rest & in transit | **Compliant** | TLS enforced (U10-FR-14); credential DPAPI-protected at rest (D-U10-02). Resolves the F1 finding. |
| SECURITY-02 Access logging on intermediaries | N/A at requirements | Caddy access logging is U3 infrastructure; the new collector is evaluated at Infrastructure Design. |
| SECURITY-03 Application-level logging | **Compliant** | U10-FR-16, U10-NFR-5. |
| SECURITY-04 HTTP security headers | N/A | No HTML-serving endpoint added. |
| SECURITY-05 Input validation | **Compliant** | U10-FR-15 body-size cap; existing `Validators.cs` covers envelope shape. |
| SECURITY-06 Least privilege | **Compliant** | §3 — the hub credential is strictly narrower than the issuing organizer. |
| SECURITY-07 Network configuration | Deferred to Infrastructure Design | The collector service's exposure must be evaluated there. |
| SECURITY-08 Application-level access control | **Compliant** | U10-FR-3 enforces per-scope authorization on both ingest routes. |
| SECURITY-09 Hardening | **Compliant** | No default credentials — every hub credential is explicitly issued. |
| SECURITY-10 Supply chain | Deferred | The OTLP packages are new dependencies; pinning and scanning are evaluated at Code Generation. Project-wide SBOM/scan gaps remain open from Build-and-Test. |
| SECURITY-11 Secure design | **Compliant** | U10-FR-15 rate limiting; abuse case considered — a stolen credential is bounded by scope, expiry, and revocation. |
| SECURITY-12 Authentication & credential management | **Compliant** | F1=B removed the plaintext exposure; hashed cloud-side, DPAPI-protected hub-side, revocable, expiring. **This was the blocking finding and it is now closed.** |
| SECURITY-13 Integrity | **Compliant** | `System.Text.Json` into a fixed DTO shape — no polymorphic deserialization of untrusted input. |
| SECURITY-14 Alerting & monitoring | **Partial — carried** | U10-FR-18 emits metrics; alert rules and retention are out of scope (§7) and remain an open project-level gap from Build-and-Test. |
| SECURITY-15 Exception handling & fail-safe | **Compliant** | U10-FR-6/7 classify rather than swallow; the breaker fails **closed** (stops sending), never open. |

### Property-Based Testing — all rules blocking
FsCheck is already in use across `shared/`, `backend/`, and `admin/` (PBT-09 satisfied). This unit
has a natural property to state at Functional Design: **for any interleaving of outages, retries,
batch splits, and restarts, the cloud log is a gap-free prefix of the hub log and contains no
duplicates.** That is the invariant the whole unit exists to preserve.

### Resiliency Baseline — all rules blocking

| Rule | Status |
|---|---|
| RESILIENCY-01 Criticality | **Compliant** — inherits U3's Medium classification; dependency direction hub → cloud is documented. |
| RESILIENCY-02 RTO/RPO | **Compliant** — U3 targets inherited (U10-NFR-7), plus a unit-specific 5-minute lag objective and completeness gate (D-U10-10). Not re-asked: U3-NFR-R1/R2 already fix the cloud targets and this unit adds no cloud workload. |
| RESILIENCY-03/04 Change mgmt, CI/CD, rollback, deployment | Inherited from U3; the new collector service is evaluated at Infrastructure Design. |
| RESILIENCY-05 Monitoring | **Compliant** — U10-FR-16/17/18, subject to U10-CON-2. |
| RESILIENCY-06 Health checks | **Compliant** — U10-FR-17 extends the existing hub `/health`. |
| RESILIENCY-08 Regional topology | Inherited (single-region, multi-zone) — unchanged. |
| RESILIENCY-10 Dependency isolation | **Compliant** — explicit timeouts (U10-NFR-3), circuit breaker (U10-FR-9), graceful degradation (offline is a no-op, U10-NFR-4). This is the rule the unit most directly serves. |
| RESILIENCY-11/12/13 DR | Inherited from U3/U7 (backup, recovery, restore-by-replay). |
| RESILIENCY-14 Resiliency testing | To be confirmed at NFR Design — the PBT property above plus the manual walkthrough are the candidate scenarios. |
| RESILIENCY-15 Incident response | Inherited/open at project level. |

---

## 9. Traceability

| Requirement | Traces to |
|---|---|
| U10-FR-1, 12, 13, 19 | **US-504** hub→cloud replication |
| U10-FR-10, 11 + U10-NFR-1, 2 | **US-602** post-event completeness |
| U10-FR-9 + U10-NFR-4 | **NFR-1.1** zero data loss, **NFR-1.2** indefinite offline operation |
| U10-FR-2..5 | New — no existing story covers hub identity. **User Stories should be executed** for this unit. |
| U10-FR-15..18 | **NFR-4.x** operability; closes part of the Build-and-Test observability gap |
| Whole unit | Unblocks **Scenario 2** (hub→cloud replication over HTTP) and **Scenario 4** (offline-first loop) in `construction/build-and-test/integration-test-instructions.md`, both currently ⛔ blocked on this adapter |

---

## 10. Recommended Next Stage

**User Stories.** The hub credential is a new user-facing concept — an organizer must issue, deliver,
and revoke it, and U10-CON-5 shows the delivery path is genuinely undecided. No existing story covers
hub identity. Under the CLAUDE.md assessment this clears the High Priority bar (new user-facing
capability, security enhancement affecting permissions, multiple components), so skipping it would be
a judgment call against the stated criteria rather than a shortcut.
