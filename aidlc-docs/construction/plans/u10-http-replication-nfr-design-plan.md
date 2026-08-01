# U10 — NFR Design Plan

**Stage**: CONSTRUCTION → NFR Design · **Unit**: U10 · **Branch**: `unit/u10-http-replication`
**Inputs**: `nfr-requirements.md`, `tech-stack-decisions.md` (TS-U10-1..9), `business-rules.md` (BR-REPL-1..50)

---

## Scope

Concrete patterns and parameters for the requirements already approved. Three items were explicitly
deferred to this stage: the **ingest rate-limit numbers** (U10-CON-3), **breaker and backoff
parameter binding**, and **metric naming**. Two more surfaced while reading the pipeline.

### Grounded before writing these questions

| Finding | Evidence | Why it matters |
|---|---|---|
| **The rate limiter runs *before* authentication** | `Program.cs:133-135` — `UseRateLimiter()`, then `UseAuthentication()`, then `UseAuthorization()` | A policy **cannot** partition by credential id: no principal exists yet. Both existing policies partition by `ctx.Connection.RemoteIpAddress`. This constrains ND-Q2. |
| **The API does not currently send `Retry-After`** | `AddRateLimiter` sets `RejectionStatusCode = 429` only; no `OnRejected` handler | `BR-REPL-31` says the hub honours the wait the cloud asks for — but nothing asks. Unless the server emits the header, that rule silently degrades to plain backoff. |

Neither is a defect in existing code; both are things this unit has to handle deliberately.

---

## PART 1a — Design Questions

---

### ND-Q1 — Shape of the ingest limit (U10-CON-3)

The limit points at our own hub. Set it too low and a post-outage drain throttles itself; too high and it protects nothing. A drain sends 500-envelope batches back to back — plausibly a few requests per second, sustained for minutes.

A) **Fixed-window rate limit**, generous — e.g. 300 requests/minute per partition. Matches the shape of the existing `login`/`registration` policies. Bounds one misbehaving caller; does nothing about many callers at once.

B) **Concurrency limit** — cap simultaneous in-flight ingest requests (e.g. 8). Protects the actual scarce resource (database connections and CPU) and never throttles a well-behaved hub, which replicates strictly sequentially. Says nothing about a single caller looping fast.

C) **Both** — a per-partition rate limit *and* a global concurrency cap. The rate limit bounds one bad actor, the concurrency cap protects the server from many. **My lean.**

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

### ND-Q2 — What partitions the ingest limit?

Constrained by the finding above: the limiter runs before authentication, so `CredentialId` is not available.

A) **Client IP**, like the existing policies. Zero new mechanism — but venue hubs sit behind NAT, so several hubs (or a hub and everything else at that venue) share one bucket. NAT is the normal case here, not the exception.

B) **A hash of the presented credential header.** Available pre-authentication with no database lookup, and gives true per-hub isolation. The raw value is never used as a key or logged — only its hash. **My lean.**

C) **Move ingest rate limiting to after authentication** so it can partition by `CredentialId`. Most precise; changes pipeline ordering for that route and means unauthenticated floods reach the auth handler before being limited.

D) **Composite of IP and credential hash.**

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### ND-Q3 — Does the cloud emit `Retry-After` on rejection?

`BR-REPL-31` has the hub honour the wait when present. Today nothing sends one.

A) **Yes — add an `OnRejected` handler** that reads the limiter's retry-after metadata and writes the header. Makes `BR-REPL-31` real rather than decorative, and any future client benefits. **My lean.**

B) **No** — the hub falls back to its own backoff. `BR-REPL-31`'s "when present" branch then never executes in practice and should be documented as dormant.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ND-Q4 — Backoff schedule

Existing `ReplicationClient` uses `100ms × 2^(attempt−1)`, up to 3 attempts.

A) **Keep it unchanged.** Least disturbance to merged U7 code.

B) **Add full jitter** — randomize each delay across `[0, computed]`. Matters here: after a venue-wide or regional outage, multiple hubs recover on the same schedule and would otherwise retry in lockstep. **My lean.**

C) **Jitter plus an absolute cap** (e.g. 30s) so a longer attempt limit could never produce an absurd delay.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ND-Q5 — DPAPI protection scope

`ProtectedData` is bound either to the machine or to the user account.

A) **`CurrentUser`** — the credential can only be unprotected by the same Windows account that installed it. Stronger. Breaks if the hub is ever run as a service under a different account than the one that installed the credential. **My lean**, since the hub is run interactively by an organizer on a laptop.

B) **`LocalMachine`** — any account on that machine can unprotect it. Survives a change of service account; weaker on a shared machine.

C) **`CurrentUser` plus additional entropy** held in configuration. Sounds stronger, but if the entropy lives beside the database it protects very little.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ND-Q6 — Where does the lag gauge get its timestamp?

`BR-REPL-45` measures the age of the oldest unreplicated event, which means reading that event's `OccurredAt` from the store.

A) **Computed during each replication run** and cached in `ReplicationStatus`. No query outside a run; the value is as-of-last-run, matching how `BR-REPL-47` already treats the pending count. **My lean.**

B) **Computed on demand** whenever status or metrics are read. Always accurate; adds a store query per health probe and per metric collection — on a hub that is also running an event.

C) **A** for the metric, **B** for the status endpoint (which is read by a human, rarely).

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

### ND-Q7 — Metric naming

A) **`eventmanager.replication.*`** — product-namespaced, OTel-style dotted lowercase. Unambiguous when this collector eventually receives more than one source. **My lean.**

B) **`replication.*`** — shorter, as sketched in Application Design; risks collision once anything else exports.

C) **OTel semantic-convention names where one fits**, custom otherwise. Most standard; there is no published convention for "application replication backlog", so most instruments would end up custom anyway.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ND-Q8 — How are the knobs configured and validated?

Configurable per FD-Q5=D and BR-REPL-27/35/38/39/40: timeout, breaker threshold and cool-down, debounce, drain interval, close-out window, batch caps, expiry-warning threshold, grace period.

A) **A bound options class with validation on start** — a bad value fails at startup with a clear message rather than at 2am mid-event. **My lean.**

B) **Plain configuration reads** with defaults, no validation.

C) **Environment variables only**, no `appsettings` section.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ND-Q9 — Resiliency testing approach (RESILIENCY-14 — extension-mandated, must not be decided for you)

The Resiliency Baseline extension requires this question at NFR Design and forbids the model from
choosing on your behalf. It asks how the resiliency mechanisms this unit builds — breaker, retry,
outage recovery, close-out completeness — will actually be validated.

A) **Use an existing DR-testing / game-day practice** — name it, and the test scenarios will be written to fit it.

B) **No practice exists — propose one**: a DR test schedule and chaos-experiment plan for adoption.

C) **Defer to the Operations phase** — capture the scenarios now, execute them later. **My lean**, and consistent with how the project has treated Operations throughout: `P-REPL-1` plus the manual docker-compose walkthrough already give this unit automated and human verification, and no live rig exists to run a game day against.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## PART 1b — Resolved Decisions



| Question | Answer | Resolution |
|---|---|---|
| ND-Q1 Limit shape | **C** | Fixed window 300/min per partition **and** a global concurrency cap of 8 |
| ND-Q2 Partition key | **B** | Hash of the presented credential header — the only per-hub option available pre-authentication |
| ND-Q3 `Retry-After` | **A** | `OnRejected` handler emits it, making BR-REPL-31 real rather than decorative |
| ND-Q4 Backoff | **A** | Retry ladder unchanged from U7; no jitter |
| ND-Q5 DPAPI scope | **A** | `CurrentUser` — service-account caveat documented as P-10 |
| ND-Q6 Lag source | **C** | Cached for `/health` and metrics; computed on demand for the human status endpoint |
| ND-Q7 Metric naming | **A** | `eventmanager.replication.*` |
| ND-Q8 Configuration | **A** | Bound options class with validate-on-start, including a cross-field rule |
| ND-Q9 Resiliency testing | **C** | Defer execution to Operations; 8 scenarios captured now, 6 covered by this unit |

**Amendment to BR-REPL-47 required by ND-Q6=C.** That rule made the pending count always
as-of-last-run. With the status endpoint computing lag on demand, returning a live lag beside a stale
count in one response would be incoherent — so the status endpoint computes **both** in a single store
pass, while `/health` and metrics keep cached values. Each surface is internally consistent, and the
on-demand path costs one scan rather than two.

**Note on ND-Q4=A.** I recommended jitter on the grounds that hubs would retry in lockstep after a
regional outage. That overstated the retry ladder's role: three attempts span roughly 300ms, so any
real synchronization comes from the 60-second drain timer and cool-down. ND-Q1=C's concurrency cap
already absorbs simultaneous arrivals server-side, so jitter would have been defence in depth rather
than a missing control. Recorded rather than quietly dropped.

---

## PART 2 — Execution Checklist

- [x] Generate `construction/u10-http-replication/nfr-design/nfr-design-patterns.md` — resilience, security, performance, and observability patterns with concrete parameters
- [x] Generate `construction/u10-http-replication/nfr-design/logical-components.md` — the NFR-bearing components and how they integrate
- [x] Record every configurable knob with its default and validation bound in one table
- [x] Confirm no pattern contradicts BR-REPL-1..50, TS-U10-1..9, or D-U10-01..15
- [x] Record extension applicability (SECURITY-01/03/05/11/12/15, RESILIENCY-05/06/10)
- [x] Record the ND-Q9 answer as the RESILIENCY-14 resiliency-testing approach and document the test scenarios that fit it
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting
- [x] Mark every checklist item [x] in the same interaction as the work
