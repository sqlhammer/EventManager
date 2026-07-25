# U3 Cloud Backend — NFR Design Patterns

**Stage**: CONSTRUCTION → NFR Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Patterns realizing the U3 NFR requirements. Decisions: Q1=A (single instance), Q2=A (synchronous inline projections), Q3=A (timeouts + bounded retry, no circuit breakers).

---

## 1. Security patterns

### SP-1 Authentication — JWT with rotating refresh (U3-NFR-S3/S7)
- **Pattern:** bearer access token (~60 min) validated by `JwtBearer` middleware on every request; refresh token (~14 d, sliding) with **rotation-on-use** persisted in PostgreSQL; logout/rotation writes a **revocation entry** so old tokens fail validation. MFA step-up gates token issuance when enabled.
- **Threat coverage:** token replay window bounded; stolen refresh detected via rotation reuse; logout truly invalidates.

### SP-2 Authorization — deny-by-default + shared RBAC policy (U3-NFR-S3, BR-RBAC-2)
- **Pattern:** an authorization filter resolves the caller's `OrganizerRoleAssignment` for the target `EventId` and calls the **U1 `RoleAuthorizationPolicy`** (same instance the hub uses). No assignment ⇒ deny. Full-Admin-only actions gated by the policy's `FullAdminOnly` set. Object-level ownership (athlete/roster/profile) checked before reads/writes.
- **Divergence prevention:** cloud and hub share one policy type — authz cannot drift.

### SP-3 Input validation gate (U3-NFR-S9, BR-X-3)
- **Pattern:** FluentValidation validators run in the request pipeline **before** controller logic and the event-write path; complex cross-field/collection rules (division no-overlap, batch conflict pre-scan, date/fee ordering) live here. EF Core parameterized queries only.

### SP-4 Abuse resistance (U3-NFR-S5/S8, BR-AUTH-2/3)
- **Pattern:** ASP.NET Core rate-limiter policies (login 5/min per IP+account; registration 10/hr per IP; ingest exempt); Identity progressive lockout (5→15 min escalating). All negative responses **generic/non-enumerating** (401/429/duplicate-email identical shape).

### SP-5 Credential hardening (U3-NFR-S4)
- **Pattern:** custom `IPasswordValidator` doing an **offline k-anonymity prefix lookup** against a bundled breached-password set; Identity adaptive hashing; TOTP secrets Identity-encrypted; secrets via env/secret-manager only.

### SP-6 Event-scoped ingest authz (U3-NFR-S14, BR-ING-1)
- **Pattern:** `EventIngestController` requires JWT + an authorization check that the caller (organizer service principal) is scoped to the batch's `EventScopeId`; foreign-event batches rejected before any append.

### SP-7 Fail-safe & hardening (U3-NFR-S11/S12)
- **Pattern:** global exception handler returns generic problem-details (no stack traces); **fail closed** on auth/validation ambiguity; security headers middleware; resource cleanup via `using`/DI scopes.

## 2. Performance patterns

### PP-1 Projection-served reads (U3-NFR-P3)
- **Pattern:** roster/results/organizer queries read **folded read-model tables**, never live event-log scans. Read models are maintained by the projection host (PP-2).

### PP-2 Synchronous inline projection (Q2=A)
- **Pattern:** the write path is `validate → AppendIfNotExists → dispatch projections → respond`, all in one request/transaction — **read-your-writes** consistency, no staleness window. Ingest folds its bounded, sequence-ordered batch inline. A `Dispatch(evt)` seam keeps a later move to async projection possible without changing callers.

### PP-3 Bounded request surface (U3-NFR-P2)
- **Pattern:** bulk-registration batch size cap (default ≤ ~200 athletes) + max request-body size guard; protects the atomic-batch path and the projection fold under burst.

### PP-4 Connection pooling (U3-NFR-P1)
- **Pattern:** Npgsql pooling tuned for the single-instance burst envelope; async I/O throughout (`async`/`await`, no sync-over-async).

## 3. Resilience patterns

### RP-1 Idempotent append (U3-NFR-R3, BR-ING-2, PBT-4)
- **Pattern:** `PostgresEventStore.AppendIfNotExistsAsync` = `INSERT … ON CONFLICT (DeviceId, SequenceNumber) DO NOTHING` (and PK on `EventId`). Replays/retries never duplicate — the cornerstone that also makes multi-instance safe later (Q1=A).

### RP-2 Idempotency keys for commands (U3-NFR-R4, BR-REG-7, BR-PAY-1/3)
- **Pattern:** `idempotency_keys` table (key → first-result hash, created-at) checked+written **inside** the command transaction; bulk-batch resubmit and payment retry return the recorded first result instead of re-executing. 30-day retention sweep.

### RP-3 Outbound timeouts + bounded retry (Q3=A, U3-NFR-R5)
- **Pattern:** every external call (payment stub, email stub) wrapped with a timeout + a small bounded retry/backoff policy; **no circuit breakers** (stubs). Payment decline/timeout maps to `Owed`+retry handle (BR-PAY-3); a **circuit-breaker seam** is documented for when a real provider replaces the stub.

### RP-4 Atomic multi-event commit (BR-EVT-2, BR-REG-6)
- **Pattern:** operations that must be all-or-nothing (event+owner assignment; atomic bulk batch) append their events within a single DB transaction; failure rolls back cleanly (fail-closed).

### RP-5 Safe migrations & rollback (U3-NFR-R7)
- **Pattern:** EF Core migrations follow **expand/contract**, backward-compatible one version, so a version-pinned image rollback is safe; no destructive auto-migrations at startup.

### RP-6 Health & degradation (U3-NFR-R6/R1)
- **Pattern:** `/health` shallow liveness + deep Npgsql readiness probe; a cloud outage degrades gracefully (hub authoritative, re-replays on reconnect) — cloud never blocks a running event.

## 4. Scalability patterns

### SC-1 Single instance, scale-ready (Q1=A, U3-NFR-X3)
- **Pattern:** one API container in Compose for MVP (vertical scale). Because idempotency and uniqueness are **enforced in the database** (RP-1/RP-2), adding instances behind a load balancer later requires no code change — the single-writer contract is honored by DB constraints, not by process singleton-ness. No sticky sessions (JWT stateless; refresh/revocation in DB).

## 5. Observability patterns

### OB-1 Structured logging (U3-NFR-S10/R9)
- **Pattern:** structured logs with timestamp/correlation-id/level; **no PII/secrets**; every authorization denial logged; request correlation id flows through the write path.
- **Metrics:** ingest depth / replication-lag, registration throughput, error rates, request latency exposed for alerting.

---

## Pattern → requirement traceability
SP-1→S3/S7 · SP-2→S3/RBAC-2 · SP-3→S9 · SP-4→S5/S8 · SP-5→S4 · SP-6→S14 · SP-7→S11/S12 · PP-1→P3 · PP-2→P1(Q2) · PP-3→P2 · RP-1→R3/ING-2 · RP-2→R4 · RP-3→R5(Q3) · RP-5→R7 · RP-6→R6/R1 · SC-1→X3(Q1) · OB-1→S10/R9.

## Extension compliance summary
| Extension | Status | Rationale |
|---|---|---|
| Security Baseline | **Compliant** | SP-1…SP-7 realize every U3 security requirement; deny-by-default + shared policy + event-scoped ingest. |
| Property-Based Testing | **Compliant** | RP-1/RP-2 give the invariants behind PBT-2/PBT-4; PP-2 seam keeps projections testable via oracle. |
| Resiliency Baseline | **Compliant** | RP-1…RP-6 cover idempotency, retry/timeout, safe migrations, health/degradation. Circuit breaker **N/A** (stubs, Q3=A); warm standby **N/A** (D-02). |
