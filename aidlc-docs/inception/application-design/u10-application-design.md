# U10 HTTP Replication Adapter — Application Design (Consolidated)

**Unit**: U10 · **Branch**: `unit/u10-http-replication` · **Stage**: INCEPTION → Application Design
**Companion documents**: [`u10-components.md`](u10-components.md) · [`u10-component-methods.md`](u10-component-methods.md) · [`u10-services.md`](u10-services.md) · [`u10-component-dependency.md`](u10-component-dependency.md)
**Decisions**: AD-Q1=A, AD-Q2=A, AD-Q3=A, AD-Q4=B, AD-Q5=C, AD-Q6=A, AD-Q7=A, AD-Q8=A, AD-Q9=C, CL-1=A, CL-2=A

---

## 1. What this design settles

**U10-CON-5 is closed.** The gap carried since Requirements Analysis — the cloud could issue a
credential and the hub could store one, but nothing connected them — is resolved by AD-Q1=A: a hub
admin endpoint on the hub's existing `OfflineOrganizerAuth`-protected surface. Provisioning is two
authenticated hops with a human in between, which suits a hub that is not addressable from the
internet.

The other structural decision: **a hub is a principal, not a person acting through a machine**
(AD-Q2=A + AD-Q3=A). The cloud gains a second authentication scheme and ingest learns to authorize
two caller kinds. The alternative — resolving a credential to its issuing organizer — was rejected
because it would attribute hub writes to someone who was not present and let a credential's reach
follow that organizer's role changes instead of its own event scope.

---

## 2. Component summary

**Cloud (7)**: `HubCredential` entity · `HubCredentialService` · `HubCredentialAuthenticationHandler` · `HubCredentialController` · `IngestCaller` · `IngestCursorQuery` (`GET /api/ingest/high-water-marks`) · ingest hardening (a new named rate-limit policy on the **existing** limiter, plus a body cap).

**Hub (10)**: `ISecretProtector`/`DpapiSecretProtector` · `HubCredentialStore` · `ReplicationCredentialController` · `HttpCloudReplicationTransport` · `ReplicationFailureClassifier` · `ReplicationCircuitBreaker` · `ReplicationSignal` · `ReplicationClient` *(amended)* · `ReplicationStatus` · `ReplicationMetrics`.

**Infrastructure (1)**: OTLP collector Compose service — exposure, logging, and image pinning decided at Infrastructure Design.

**Unchanged by design**: `shared/` entirely · `IReplicationProtocol` · `StoreBackedReplicationTransport` · `EventAuthorizer`/`ReadAuthorizer` · the `OrganizerAction` enum · U4a pairing and device registry.

---

## 3. Requirement coverage

| U10-FR | Owning component(s) |
|---|---|
| 1 HTTP transport | `HttpCloudReplicationTransport` |
| 2 Credential issuance | `HubCredentialService`, `HubCredentialController`, `HubCredential` |
| 3 Per-event authorization | `HubCredentialAuthenticationHandler`, `IngestCaller`, `IngestService` |
| 4 Revoked/expired refused | `HubCredentialService`, `ReplicationFailureClassifier` |
| 5 Protected local storage | `HubCredentialStore`, `ISecretProtector`, `ReplicationCredentialController` |
| 6 Failure classification | `ReplicationFailureClassifier` |
| 7 Retry only transient | `ReplicationClient.SendWithRetryAsync` |
| 8 Honour throttling wait | `ReplicationFailureClassifier`, `ReplicationClient` |
| 9 Circuit breaker | `ReplicationCircuitBreaker`, `HttpCloudReplicationTransport.IsOnline` |
| 10 Three triggers | `ReplicationSignal`, `ReplicationClient` (timer), `ReplicationCredentialController` (close-out) |
| 11 Close-out completeness | `ReplicationClient.FlushForCloseOutAsync` |
| 12 Cursor seeding | `IngestCursorQuery`, `HttpCloudReplicationTransport.GetHighWaterMarksAsync`, `ReplicationClient.SeedCursorsAsync` |
| 13 Batch caps and splitting | `ReplicationClient` + `IReplicationProtocol` (unchanged) |
| 14 TLS enforced | `HttpCloudReplicationTransport` construction check |
| 15 Body cap + rate limit | Ingest hardening |
| 16 Structured logging | All hub replication components |
| 17 Health status | `ReplicationStatus` |
| 18 Metrics | `ReplicationMetrics`, collector |
| 19 Idempotent, gap-free | `ReplicationClient` + `IngestService` (both unchanged in this respect) |

All 19 have an owning component.

---

## 4. Constraint status

| Constraint | Status |
|---|---|
| **U10-CON-1** DPAPI platform coupling | **Addressed** — `ISecretProtector` in the library, DPAPI registered only by the host, so the library stays platform-neutral |
| **U10-CON-2** Collector blind during an outage | **Accepted and mitigated** — `ReplicationStatus` is the venue-visible signal and never depends on reaching the cloud |
| **U10-CON-3** Rate limit points at our own hub | **Carried to NFR Design** — the concrete limit is a number this stage does not own; `ReplicationFailureClassifier` treating throttling as transient is the safety net for a mis-set value |
| **U10-CON-4** Cross-solution coupling | **Addressed** — confined to `tests/EventManager.Integration.Tests` in its own solution (CL-2=A); no production cross-reference |
| **U10-CON-5** Credential delivery | **CLOSED** — AD-Q1=A |
| **U10-CON-6** Merged U7 code | **Amended, not closed** — AD-Q4=B widened the `ReplicationClient` edit from retry classification to also owning the schedule and a service lifetime. The 17 existing admin tests are the gate |

---

## 5. Extension applicability at this stage

| Rule | Status |
|---|---|
| SECURITY-06 Least privilege | **Compliant** — a hub credential's reach is one event scope and two routes; the `OrganizerAction` enum was deliberately not extended |
| SECURITY-08 Access control | **Compliant** — scheme-per-route; ingest authorizes the caller's own scope; failures disclose nothing about why |
| SECURITY-11 Secure design | **Compliant** — `HubCredentialService` isolates the security-critical logic; the abuse case (stolen credential) is bounded by scope, expiry, and revocation; `ReplicationFailureClassifier` is a pure function so its security-relevant behaviour is trivially testable |
| SECURITY-12 Credential management | **Compliant** — hashed cloud-side, DPAPI-protected hub-side, returned exactly once, never logged or echoed |
| SECURITY-03/15 Logging, fail-safe | **Compliant by design** — the breaker fails closed (stops sending); no credential in any log, metric tag, or status response |
| RESILIENCY-10 Dependency isolation | **Compliant** — explicit timeout, circuit breaker, and three documented degraded modes, all non-fatal |
| SECURITY-01/02/07, RESILIENCY-05 | **Deferred to Infrastructure Design** — collector exposure, access logging, TLS termination |
| SECURITY-10 Supply chain | **Deferred to NFR Requirements / Code Generation** — the OTel packages are the only genuinely new dependencies; the rate limiter is already in use |

No blocking findings at this stage.

---

## 6. The one thing most likely to go wrong

`ReplicationClient` becomes a singleton `BackgroundService` while `IEventStore` and `HubDbContext`
stay scoped (`admin/EventManager.Hub/Program.cs:16,40`). CL-1=A resolves this with a per-run scope
from `IServiceScopeFactory`.

This is called out separately because of how it fails. A captive scoped `DbContext` in a singleton
does not throw at startup and does not fail a happy-path test — it corrupts intermittently under
concurrent access, on a component whose entire purpose is guaranteeing no data is lost. Functional
Design and Code Generation should both treat it as a named risk rather than an implementation detail.
