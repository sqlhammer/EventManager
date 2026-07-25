# U3 Cloud Backend — NFR Design Plan

**Stage**: CONSTRUCTION → NFR Design (per-unit)
**Unit**: U3 Cloud Backend (`backend/`)
**Date**: 2026-07-25 · Stage-by-stage

---

## Context

NFR Requirements pinned the mechanisms (TSD-1…10 + S/R/P/T parameters). NFR Design turns those into **design patterns** and **logical components**. Most patterns are therefore already determined; the questions below cover the few genuinely design-shaping choices the requirements left open. Categories evaluated per the rule:

| Category | Status |
|---|---|
| **Security patterns** | Determined by NFR-Req (JWT+refresh rotation+revocation, deny-by-default RBAC via U1 policy, event-scoped ingest authz, FluentValidation gate, rate limiter, offline breach check). No open question. |
| **Performance patterns** | Mostly determined (projection-served reads, request-size cap). One open question: projection update timing (Q2). |
| **Resilience patterns** | Idempotency + retry/backoff + timeouts + expand/contract migrations determined. Open: outbound-call resilience depth (Q3). |
| **Scalability patterns** | Open: API instance model vs the event-store single-writer contract (Q1). |
| **Logical components** | Derived once Q1–Q3 answered (projection host, token service, ingest pipeline, idempotency store, validators, health/rate-limit middleware). |

---

## Open design decisions (please answer inline)

> Multiple-choice, `[Answer]:` tag; recommendation **(rec)**.

### Q1 — API instance model vs. event-store single-writer contract (P-7)
`IEventStore` has a single-writer contract; `AppendIfNotExists` is idempotent via a DB unique constraint. How do we run the API for MVP?

- **A. (rec)** **Single API container** in Compose for MVP (vertical scale only). PostgreSQL unique `(DeviceId, SequenceNumber)` + `ON CONFLICT DO NOTHING` makes appends safe **regardless** of instance count, so the design stays **horizontal-scale-ready** without building it now. Matches Medium criticality + "sized for registration bursts, not event-day load" (NFR-5.4) and direct/in-place deploy (NFR-3.6).
- **B.** Design for **multiple API instances now** (stateless nodes behind a load balancer, DB-enforced serialization). More scalable, but adds LB/session/config surface unjustified at the 300-athlete / hundreds-concurrent envelope for MVP.

[Answer]: A

### Q2 — Projection update timing (write & ingest paths)
After appending events, when are read models (roster, results) updated?

- **A. (rec)** **Synchronous inline** — append then dispatch projections within the same request/transaction (the pattern already shown in tech-env.md and services.md S-1). Simple, read-your-writes consistent, adequate at this scale. Ingest batches are bounded and ordered, so inline folding is fine.
- **B.** **Asynchronous background projector** — append fast, fold read models in a background worker (eventual consistency). Better under heavy ingest, but adds a worker component, lag monitoring, and staleness windows not needed for MVP load.
- **C.** Hybrid: synchronous for interactive writes (registration/RBAC), background for large ingest batches. Most complex; premature.

[Answer]: A

### Q3 — Outbound-call resilience depth (payment stub, email stub)
NFR-3.8 mandates timeouts + bounded retry. How much resilience machinery for U3's outbound calls?

- **A. (rec)** **Timeouts + bounded retry/backoff only** (e.g., via a small Polly policy or built-in), no circuit breakers. The payment provider is an in-process **stub** (D-06) and email is a **log stub** — a circuit breaker guards nothing real in MVP. Decline/timeout already map to `Owed`+retry (BR-PAY-3). Circuit-breaker seam noted for when a real provider lands.
- **B.** **Add circuit breakers now** (Polly) around payment/email. Future-proof, but over-engineered against stubs; adds tuning surface.

[Answer]: A

### Q4 — Anything else / overrides?
Free-form: any pattern to add (e.g., outbox for eventual real email/replication-ack, read-model caching, connection-pool sizing) or constraint to note?

[Answer]: N/A

---

## Execution checklist (after answers approved)

- [x] Q1–Q3 answered (all A) + Q4 N/A
- [x] `nfr-design/nfr-design-patterns.md` — SP-1..7 (security), PP-1..4 (performance), RP-1..6 (resilience), SC-1 (scalability), OB-1 (observability); traceability + extension compliance
- [x] `nfr-design/logical-components.md` — full U3 component map, per-component responsibilities, S-1/S-2/S-7 wiring, deployment shape, traceability
- [x] Extension compliance summary (Security/PBT/Resiliency Compliant; circuit breaker + warm standby N/A)
- [ ] Completion message; await explicit approval
