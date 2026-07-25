# U3 Cloud Backend — NFR Requirements

**Stage**: CONSTRUCTION → NFR Requirements · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25

U3-scoped view of the project NFR baseline (requirements.md NFR-1…6) plus the unit-level parameter decisions (plan Q1–Q6, all = recommended). IDs prefixed `U3-NFR-` reference the project NFR they realize.

---

## 1. Security (Security Baseline — full, blocking)

| ID | U3 requirement | Realizes | Backing rule |
|---|---|---|---|
| **U3-NFR-S1** | All API traffic over **TLS 1.2+**; HTTP redirected to HTTPS; HSTS in production. | NFR-2.2 | BR-X-3 |
| **U3-NFR-S2** | **PostgreSQL at-rest encryption at the storage/volume layer** (encrypted Docker volume / provider-managed encrypted disk) — **Q5=A**. Column-level pgcrypto deferred post-MVP. MFA secrets remain Identity-encrypted regardless. | NFR-2.3 | — |
| **U3-NFR-S3** | **Deny-by-default** on every endpoint; **JWT validated on every request**; per-event RBAC via the **U1 `RoleAuthorizationPolicy`** instance; object-level ownership checks (athlete/roster/profile). | NFR-2.5 | BR-RBAC-2, BR-AUTH-8, BR-RES-1 |
| **U3-NFR-S4** | Password policy: **≥ 8 chars + breached-password check via an offline bundled k-anonymity dataset** (**Q1=A**); adaptive hashing = Identity default. No external runtime dependency on the registration hot path. | NFR-2.6 | BR-AUTH-1 |
| **U3-NFR-S5** | **Progressive brute-force lockout**: 5 failed logins → 15-min lock, escalating on repeat (**Q3=A**). | NFR-2.6 | BR-AUTH-2 |
| **U3-NFR-S6** | **MFA (TOTP, RFC 6238)** for organizer accounts; enrollment QR + recovery codes; disable requires re-auth. | NFR-2.6 | BR-AUTH-5/7 |
| **U3-NFR-S7** | **JWT lifetimes** (**Q2=A**): access ~60 min; refresh ~14 days **sliding, rotated on use**; server-side **revocation list** so logout invalidates. | NFR-2.5/2.6 | BR-AUTH-6 |
| **U3-NFR-S8** | **Rate limiting** (ASP.NET Core rate limiter, **Q3=A**): login **5/min per IP+account**, registration **10/hour per IP**; ingest exempt (authenticated service principal). 429 responses generic/non-enumerating. | NFR-2.10 | BR-AUTH-3 |
| **U3-NFR-S9** | **Input validation before the write path**: FluentValidation on complex request models (registration, bulk batch, event/division config), DataAnnotations on simple ones; EF Core parameterized queries only. | NFR-2.4 | BR-X-3 |
| **U3-NFR-S10** | **Structured logging** (timestamp, correlation ID, level), **no PII/secrets**; every authorization **denial logged** (audit). Domain event log is append-only + auditable (who/what/when). | NFR-2.7/2.11 | BR-RBAC-6, BR-X-1 |
| **U3-NFR-S11** | **Hardening**: security headers on any HTML-serving path, **generic production errors (no stack traces)**, no default creds, secrets via env/secret-manager only. | NFR-2.8 | — |
| **U3-NFR-S12** | **Fail-safe**: global exception handler; **fail closed**; resource cleanup on error paths. | NFR-2.12 | BR-PAY-3 |
| **U3-NFR-S13** | **Supply chain**: locked NuGet versions, CI vulnerability scan, **pinned Docker base image**, SBOM for the backend image. | NFR-2.9 | — |
| **U3-NFR-S14** | **Ingest authz** (**Q7=A** from functional design): `EventIngestController` requires JWT + **event-scoped** organizer service principal (`EventScopeId` match); foreign-event ingest rejected. | NFR-2.5 | BR-ING-1 |

## 2. Reliability / Resiliency (Resiliency Baseline — directional, blocking)

| ID | U3 requirement | Realizes | Backing rule |
|---|---|---|---|
| **U3-NFR-R1** | Cloud backend criticality = **Medium**; a cloud outage must **not** stop a running event (hub is authoritative; re-replays on reconnect). | NFR-3.1 | BR-ING-2 |
| **U3-NFR-R2** | Targets: **99.5% availability**, **RTO ≤ 4h** (Compose redeploy + restore), **RPO ≤ 24h** for cloud-originated data (accounts/registrations) via daily backups; event-day data RPO≈0 via hub re-replay. | NFR-3.2 | — |
| **U3-NFR-R3** | **Ingest idempotency & resumability**: `AppendIfNotExists`, sequence-ordered per device; safe under retry/backoff; `IngestResult` returns high-water mark so the hub resumes gap-free. | NFR-3.8, FR-4.6 | BR-ING-2 |
| **U3-NFR-R4** | **Idempotency-key store** (**Q4=A**): dedicated PostgreSQL table (key → first-result hash, created-at), **30-day retention**, checked in the same transaction as the write — covers bulk-batch keys and payment retries. | NFR-3.x | BR-REG-7, BR-PAY-3 |
| **U3-NFR-R5** | **Timeouts on all external calls** (payment provider, email stub); bounded retry where applicable; graceful degradation. | NFR-3.8 | BR-PAY-3 |
| **U3-NFR-R6** | **Health checks**: shallow `/health` (liveness) + deep **DB-connectivity** readiness check. | NFR-3.7 | — |
| **U3-NFR-R7** | **EF migrations backward-compatible one version (expand/contract)** so a version-pinned image rollback is safe; migrations never auto-run destructively. | NFR-3.5 | — |
| **U3-NFR-R8** | **Backups**: automated **daily PostgreSQL backup**, retention ≥ 30 days, encrypted; documented restore-validation procedure (runbook produced in Infra Design/Build&Test). | NFR-3.10 | — |
| **U3-NFR-R9** | **Monitoring**: structured logs + key metrics (**replication-lag / ingest depth**, error rates, request latency) with alerting hooks. | NFR-3.9 | — |

## 3. Performance & Scale

| ID | U3 requirement | Realizes |
|---|---|---|
| **U3-NFR-P1** | Sized for **pre-event registration bursts (hundreds of concurrent users)**, not event-day load. Registration/login p95 < ~500 ms under nominal burst (excluding deliberate rate-limit/lockout). | NFR-5.4/5.1 |
| **U3-NFR-P2** | Bulk registration batch handles a full academy roster in one atomic request; a **request-size cap** guards the endpoint (concrete cap set in NFR Design; default ≤ ~200 athletes/batch, comfortably above the 300-athlete event envelope per team). | NFR-5.1 |
| **U3-NFR-P3** | Projection reads (roster, results) serve from folded read models, not live event-log scans, to keep organizer roster views responsive. | NFR-5.4 |

## 4. Testing & Quality (PBT — full, blocking)

| ID | U3 requirement | Realizes |
|---|---|---|
| **U3-NFR-T1** | **xUnit** + **FsCheck**; **80%+ coverage** on U3 core logic (registration/assignment/RBAC/ingest projection); lighter on plumbing. | NFR-4.1/4.2 |
| **U3-NFR-T2** | **U3 mandatory properties** (from functional design): **PBT-1** division-assignment determinism/order-independence; **PBT-2** no double-registration across batches/resubmits; **PBT-3** RBAC deny-by-default; **PBT-4** ingest idempotency (any order/partition/repetition ⇒ identical log+projection). Seeds logged; shrinking enabled. | NFR-4.3 |
| **U3-NFR-T3** | Example-based tests pin business-critical scenarios (bulk conflict itemization, last-admin guard, payment decline→Owed, email-confirm gating). | NFR-4.3 |
| **U3-NFR-T4** | **CI gates**: build + unit/PBT tests + coverage threshold block merge (GitHub Actions). | NFR-3.4/4.4 |

## 5. Platform & Structure

| ID | U3 requirement | Realizes |
|---|---|---|
| **U3-NFR-X1** | **C# 13 / .NET 10**, ASP.NET Core Web API; EF Core **Npgsql** on **PostgreSQL**; lives in `backend/` alongside U8 `EventManager.Payments`. | NFR-6.1/6.2 |
| **U3-NFR-X2** | **Snowflake IDs** for all cloud-owned entities (cloud worker range), stored as **BIGINT**; single `IIdGenerator` from `EventManager.Sync`. | NFR-6.5 |
| **U3-NFR-X3** | **Deployment**: Docker image + **Docker Compose (API + PostgreSQL)**, provider-agnostic, no cloud IaC in MVP. | NFR-6.4 |

---

## Extension compliance summary

| Extension | Status | Rationale |
|---|---|---|
| **Security Baseline** | **Compliant** | Every SECURITY-mapped NFR (2.1–2.12) has a U3 requirement (U3-NFR-S1…S14). LAN-transport items (NFR-2.1) are **N/A** to U3 (hub concern) — no card data stored (U8 stub, D-06). |
| **Property-Based Testing** | **Compliant** | FsCheck mandated; 4 U3 properties defined (U3-NFR-T2) atop the project property categories (NFR-4.3). |
| **Resiliency Baseline** | **Compliant** | Targets, ingest idempotency/resumability, idempotency store, timeouts, health checks, backward-compatible migrations, backups, monitoring all specified (U3-NFR-R1…R9). Warm-standby/failover = **N/A** for MVP (D-02, Medium criticality). |

## Carry-forward to NFR Design
Concrete design patterns for: the offline breached-password dataset lookup, JWT issuance/rotation/revocation-list mechanics, the rate-limiter policies wiring, the idempotency-table transaction pattern, the projection-host structure, and the ingest resumability protocol. Infra Design covers Compose topology, encrypted volume, backup job, and health endpoints.
