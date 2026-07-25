# U3 Cloud Backend — NFR Requirements Plan

**Stage**: CONSTRUCTION → NFR Requirements (per-unit)
**Unit**: U3 Cloud Backend (`backend/`)
**Date**: 2026-07-25 · Stage-by-stage

---

## Context: most NFRs are already settled at project level

`aidlc-docs/inception/requirements/requirements.md` fixes a comprehensive NFR baseline that **binds U3**. This stage **maps** those to U3 concretely and decides only the handful of unit-level parameters left open. Already-decided (not re-asked):

| Area | Already fixed | Ref |
|---|---|---|
| Criticality / targets | Cloud = **Medium**; availability **99.5%**, **RTO ≤ 4h**, **RPO ≤ 24h** cloud-originated data (event-day RPO≈0 via hub re-replay) | NFR-3.1/3.2 |
| Transport | TLS 1.2+ on all API traffic | NFR-2.2 |
| At rest | PostgreSQL storage encryption enabled | NFR-2.3 |
| AuthN/Z | Deny-by-default, JWT validated every request, per-event RBAC (reuse U1 policy), object-level ownership | NFR-2.5 |
| Credentials | Adaptive hashing (Identity default), breached-password check, brute-force lockout, MFA for organizers, secrets via env/secret-manager | NFR-2.6 |
| Input validation | FluentValidation/DataAnnotations before write path; EF parameterized queries | NFR-2.4 |
| Logging/audit | Structured logs, no PII/secrets; append-only auditable event log | NFR-2.7/2.11 |
| Rate limiting | Required on public endpoints (registration, login) | NFR-2.10 |
| Hardening | Security headers, generic prod errors, no stack traces, fail-closed | NFR-2.8/2.12 |
| Supply chain | Locked deps, CI vuln scan, pinned Docker base, SBOM | NFR-2.9 |
| Testing/PBT | xUnit; **FsCheck**; 80% coverage on core; mandated property categories | NFR-4.1/4.2/4.3 |
| Performance/scale | 300 athletes; cloud sized for **pre-event registration bursts (hundreds concurrent)**, not event-day load | NFR-5.1/5.4 |
| Platform | C# 13/.NET 10, ASP.NET Core Web API, EF Core Npgsql/PostgreSQL, Snowflake IDs (BIGINT) | NFR-6.1/6.5 |
| Deployment | Docker image + Docker Compose (API + PostgreSQL), provider-agnostic, no IaC in MVP | NFR-6.4 |
| CI/CD & rollback | GitHub Actions build+test+coverage gate; version-pinned image; **backward-compatible one-version EF migrations (expand/contract)** | NFR-3.4/3.5 |
| Health | Shallow `/health` + deep DB-connectivity check | NFR-3.7 |

The tech stack for U3 is therefore essentially **fixed by tech-env.md + NFR-6**. This stage produces `nfr-requirements.md` (U3-scoped NFR table) and `tech-stack-decisions.md` (library-level picks), and asks only the open parameters below.

---

## Open unit-level decisions (please answer inline)

> Multiple-choice, `[Answer]:` tag; recommendation marked **(rec)**. These are concrete values/mechanisms the project baseline left to the unit.

### Q1 — Breached-password check source (NFR-2.6)
The password policy mandates a breached-password check. How is it performed?

- **A. (rec)** **Offline local dataset** — bundle a hashed breached-password list (k-anonymity prefix lookup) inside the container; no runtime external call. Matches the provider-agnostic / "no third-party dependency" posture and keeps registration self-contained + fast under burst load (NFR-5.4).
- **B.** **Live HaveIBeenPwned range API** at registration/password-change (k-anonymity, no full password sent). Always current, but adds an external runtime dependency + latency on the registration hot path.
- **C.** Identity default validators only (length/complexity), **no** breach check in MVP. Contradicts NFR-2.6 — not recommended.

[Answer]: A

### Q2 — JWT lifetimes & refresh strategy (NFR-2.5/2.6)
Token TTLs and refresh handling aren't pinned by the baseline.

- **A. (rec)** Short **access token (~60 min)** + **sliding refresh token (~14 days)** with **rotation on use** and a server-side **revocation list** so logout (BR-AUTH-6) truly invalidates. Balances UX vs exposure; supports MFA step-up.
- **B.** Long-lived access token (e.g., 24h), no refresh rotation. Simpler, weaker on revocation/logout.
- **C.** Access token only, no refresh (re-login on expiry). Simplest, poorest UX.

[Answer]: A

### Q3 — Concrete rate-limit & lockout parameters (NFR-2.10, NFR-2.6, BR-AUTH-2)
The baseline requires rate limiting + progressive lockout but not values.

- **A. (rec)** Built-in **ASP.NET Core rate limiter**: login **5/min per IP+account** (fixed window), registration **10/hour per IP**, ingest exempt (authenticated service principal). Identity lockout: **5 failed attempts → 15-min lock, escalating** on repeat. Generic 429/lockout responses (non-enumerating, BR-AUTH-3).
- **B.** Looser dev-friendly limits (e.g., login 20/min), same mechanism. Easier local testing, weaker protection.
- **C.** Specify different values (state them in the answer).

[Answer]: A

### Q4 — Idempotency-key persistence (BR-REG-7, BR-PAY-1/3)
Bulk-registration batch keys and payment idempotency keys need a store.

- **A. (rec)** Dedicated **PostgreSQL idempotency table** (key → first-result hash, created-at), **30-day retention**, checked inside the same transaction as the write. Durable across restarts; consistent with the RPO-24h backup regime.
- **B.** In-memory/distributed cache with TTL. Faster, but keys lost on restart → weaker guarantee under the atomic-batch rule.

[Answer]: A

### Q5 — PostgreSQL at-rest encryption approach (NFR-2.3)
"Storage encryption enabled" — at what layer for MVP?

- **A. (rec)** **Storage/volume-level encryption** (encrypted Docker volume / provider-managed encrypted disk); provider-agnostic, zero app-layer complexity, satisfies NFR-2.3 for MVP. Column-level encryption of specific fields deferred post-MVP.
- **B.** **Column-level (pgcrypto)** encryption of sensitive fields now (e.g., MFA secrets already Identity-encrypted; add for others). More granular, more code + key-management burden in MVP.

[Answer]: A

### Q6 — Anything else / overrides?
Free-form: any U3 NFR to add, tighten, or relax (e.g., a specific registration-burst throughput number beyond "hundreds concurrent", request-size limits on bulk batch, log-retention specifics)?

[Answer]: N/A

---

## Execution checklist (after answers approved)

- [x] Q1–Q5 answered (all A) + Q6 N/A; no vague answers, no follow-ups
- [x] `nfr-requirements/nfr-requirements.md` — U3 NFR tables: Security S1–S14, Resiliency R1–R9, Performance P1–P3, Testing T1–T4, Platform X1–X3; extension compliance summary; carry-forward to NFR Design
- [x] `nfr-requirements/tech-stack-decisions.md` — confirmed fixed stack + 10 library-level decisions (TSD-1..10) with rationale + rejected alternatives; pinned dependency summary
- [x] Extension compliance summary (Security Baseline / PBT / Resiliency all Compliant; LAN + warm-standby items N/A with rationale)
- [ ] Completion message; await explicit approval
