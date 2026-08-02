# U10 — NFR Design Patterns

**Unit**: U10 HTTP Replication Adapter · **Stage**: NFR Design
**Answers**: ND-Q1=C, ND-Q2=B, ND-Q3=A, ND-Q4=A, ND-Q5=A, ND-Q6=C, ND-Q7=A, ND-Q8=A, ND-Q9=C

---

## 1. Resilience patterns

### P-1 · Classified retry (`BR-REPL-29..33`)
Retry ladder kept **unchanged** from U7 (ND-Q4=A): `100ms × 2^(attempt−1)`, 3 attempts, no jitter.
What changes is *what* is retried — only transient and throttled failures. A permanent failure
propagates immediately and consumes no attempt.

**Residual risk, and why it is acceptable.** I argued for jitter on the grounds that hubs recovering
from a regional outage would retry in lockstep. On reflection that overstated the retry ladder's
role: with 3 attempts the whole ladder spans ~300ms, so synchronization would come from the 60-second
drain timer and breaker cool-down, not from retries. And **P-4's concurrency cap already handles the
server side of that** — simultaneous arrivals queue rather than overwhelm. Jitter would have been
defence in depth, not a missing control.

### P-2 · Circuit breaker (`BR-REPL-34..36`)
Counts **connection** failures only. Opens at 3 consecutive; after a 60-second cool-down permits
exactly one trial request; success closes and resets, failure re-opens. An open breaker makes
replication a no-op, never an error.

### P-3 · Timeout on every call (`BR-REPL-27`)
30 seconds, configurable. No unbounded wait, per RESILIENCY-10.

### P-4 · Bulkhead — global ingest concurrency (ND-Q1=C)
A concurrency limiter caps simultaneous in-flight ingest requests at **8**. This protects the actual
scarce resource — database connections and CPU — and never throttles a well-behaved hub, which
replicates strictly sequentially. It is the control that makes many hubs arriving at once a queuing
problem rather than an outage.

### P-5 · Rate limit — per-hub bound (ND-Q1=C)
Fixed window, **300 requests/minute** per partition. A post-outage drain runs at a few requests per
second, so a conforming hub has roughly an order of magnitude of headroom; a single looping caller is
bounded.

### P-6 · Graceful degradation
Three degraded modes, all non-fatal: no credential installed → hub runs and reports why; breaker open
→ replication is a no-op; cloud unreachable at startup → start with empty cursors and proceed.

---

## 2. Security patterns

### P-7 · Two authentication schemes, declared per route (AD-Q2=A)
JWT for people, `"HubCredential"` for hubs. A route states which it accepts, so the two principal
types cannot be confused by accident.

### P-8 · Pre-authentication rate-limit partitioning (ND-Q2=B)
**Constraint, verified**: `app.UseRateLimiter()` runs at `Program.cs:133`, *before*
`UseAuthentication()` — so no policy can partition by `CredentialId`.

The ingest policy partitions by a **hash of the presented credential header**. This gives true
per-hub isolation without a database lookup and without resolving a principal. Client IP was rejected
because venue hubs sit behind NAT, which for this product is the normal case, not an edge case.

**Rules this pattern carries**: the raw header value is never used as a partition key, never logged,
and never appears in diagnostics — only its hash. A request with no credential header partitions to a
single shared `"anonymous"` bucket, so unauthenticated floods cannot consume per-hub capacity.

### P-9 · Honest throttling (ND-Q3=A)
**Constraint, verified**: the API sets `RejectionStatusCode = 429` but registers no `OnRejected`
handler, so it does not currently emit `Retry-After`.

An `OnRejected` handler is added that reads the limiter's retry-after metadata and writes the header.
Without it, `BR-REPL-31` — "honour the wait the cloud asks for" — would be decorative, since nothing
would ever ask.

### P-10 · Machine-and-user-bound secret protection (ND-Q5=A)
DPAPI with `DataProtectionScope.CurrentUser`. A copied hub database is useless on another machine
**and** under another account on the same machine.

**Documented caveat**: if the hub is ever run as a Windows service under a different account than the
one that installed the credential, unprotection fails. The failure is clean and detectable — it
surfaces as "no usable credential" rather than as corruption — and the remedy is to re-install the
credential under the running account. Recorded here so it is diagnosed in seconds rather than hours.

### P-11 · Hash-only credential storage, generic failures
The cloud stores only a salted hash (`BR-REPL-3`). Every authentication failure — unknown, expired,
revoked, malformed — produces one indistinguishable result (`BR-REPL-7`), so the endpoint cannot be
used to probe which credentials exist.

---

## 3. Performance patterns

### P-12 · Bounded batching (`BR-REPL-28`)
At most **500 envelopes** and at most **4 MB** per request; oversized batches split. The server's
ingest body limit is **8 MB**, deliberately twice the client cap — a conforming hub can never trip it,
so a `413` unambiguously means a non-conforming caller.

### P-13 · Two-tier status freshness (ND-Q6=C)

| Surface | Source | Why |
|---|---|---|
| `/health` and exported metrics | Values cached from the last replication run | Probed frequently and scraped on an interval; a store query per probe on a machine running a live event is not worth the accuracy |
| `GET /api/replication/status` | Computed on demand | Read by a human, rarely, and usually because something looks wrong — which is exactly when a stale answer is least useful |

**Amendment to `BR-REPL-47`.** That rule said the pending count is always as-of-last-run. Under
ND-Q6=C the human-facing status endpoint computes lag on demand, and it would be incoherent to return
a live lag next to a stale count in the same response. **The status endpoint therefore computes lag
*and* pending together in one store pass**; `/health` and metrics keep the cached values. Each surface
is internally consistent, and one query yields both figures, so the on-demand path costs a single
scan rather than two.

---

## 4. Observability patterns

### P-14 · Instrumentation namespace (ND-Q7=A)
`eventmanager.replication.*` — product-namespaced, OTel-style dotted lowercase, unambiguous once the
collector receives more than one source.

| Instrument | Kind | Notes |
|---|---|---|
| `eventmanager.replication.events.sent` | Counter | |
| `eventmanager.replication.batches` | Counter | |
| `eventmanager.replication.failures` | Counter | Tagged by failure kind — **never** by credential |
| `eventmanager.replication.backlog` | Gauge | Cached, per P-13 |
| `eventmanager.replication.lag.seconds` | Gauge | Cached, per P-13; zero when no backlog (`BR-REPL-45`) |
| `eventmanager.replication.circuit.open` | Gauge | 0/1 |

No instrument, tag, or label carries credential material (`U10-NFR-5`).

### P-15 · Venue-visible status is local-only (`BR-REPL-48`, U10-CON-2)
All status is computed in-process. The cloud collector is unreachable during an outage — the exact
situation the unit exists to survive — so it can never be the primary signal. `/health` is.

---

## 5. Configuration (ND-Q8=A)

A bound options class with **validation on start**: a bad value fails at startup with a clear
message, rather than at 2am mid-event.

| Knob | Default | Valid range | Rule |
|---|---|---|---|
| Request timeout | 30 s | 1 s – 5 min | `BR-REPL-27` |
| Breaker threshold | 3 failures | 1 – 20 | `BR-REPL-35` |
| Breaker cool-down | 60 s | 5 s – 30 min | `BR-REPL-35` |
| Append debounce | 2 s | 0 s – 60 s | `BR-REPL-38` |
| Drain interval | 60 s | 5 s – 30 min | `BR-REPL-39` |
| Close-out window | 120 s | 10 s – 30 min | `BR-REPL-40` |
| Max envelopes per batch | 500 | 1 – 5000 | `BR-REPL-28` |
| Max batch bytes | 4 MB | 64 KB – 16 MB | `BR-REPL-28` |
| Max retry attempts | 3 | 1 – 10 | `BR-REPL-33` |
| Expiry warning threshold | 7 days | 1 – 90 days | `BR-REPL-16` |
| Credential grace period *(cloud)* | 14 days | 1 – 90 days | `BR-REPL-4` |
| Ingest rate limit | 300/min | 10 – 10000 | P-5 |
| Ingest concurrency | 8 | 1 – 128 | P-4 |
| Allow insecure base URL | `false` | — | `BR-REPL-26`; **must never be true outside development** |
| Cloud base URL | *(none)* | must be absolute; HTTPS unless the flag above | `BR-REPL-26` |

Cross-field validation: max batch bytes must be **strictly less than** the server's ingest body limit,
so P-12's guarantee holds rather than being a convention.

---

## 6. Resiliency testing (RESILIENCY-14, ND-Q9=C)

**Approach: defer execution to the Operations phase, capture the scenarios now.**

| # | Scenario | Verified how, and when |
|---|---|---|
| R-1 | Connectivity lost mid-event; restored later | `P-REPL-1` property, automated, this unit |
| R-2 | Breaker opens and later recovers | Unit test, this unit |
| R-3 | Hub restarts mid-event | `P-REPL-1`, automated, this unit |
| R-4 | Credential revoked mid-event | Cross-solution credential-path test, this unit |
| R-5 | Cloud throttles a large drain | Unit test with a `429` + `Retry-After` stub |
| R-6 | Full event with zero internet, then reconnect and close out | **Manual walkthrough** (Q11=D), this unit |
| R-7 | Cloud down for hours; hub keeps running | Operations game day — no live rig exists to run it against |
| R-8 | Collector unavailable while the hub is online | Operations — must not affect replication |

R-1 to R-6 are covered by this unit. **R-7 and R-8 are not, and are deferred rather than claimed.**

---

## 7. Extension compliance at this stage

| Rule | Status |
|---|---|
| SECURITY-01 | **Compliant** — `BR-REPL-26`, cross-validated config |
| SECURITY-03 | **Compliant** — P-14; no credential in any tag or label |
| SECURITY-05 | **Compliant** — P-12 body limit, options range validation |
| SECURITY-11 | **Compliant** — P-4 and P-5 layered; abuse case bounded |
| SECURITY-12 | **Compliant** — P-10, P-11 |
| SECURITY-15 | **Compliant** — breaker fails closed; degraded modes are no-ops, not open failures |
| RESILIENCY-05 | **Compliant** — P-14, P-15, subject to U10-CON-2 |
| RESILIENCY-06 | **Compliant** — P-13 extends the existing hub `/health` |
| RESILIENCY-10 | **Compliant** — P-1, P-2, P-3, P-4, P-6 |
| RESILIENCY-14 | **Compliant** — approach chosen by the user (ND-Q9=C), scenarios captured in §6 |

**No blocking findings.**
