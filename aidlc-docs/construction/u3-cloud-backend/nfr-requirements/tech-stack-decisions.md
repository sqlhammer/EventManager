# U3 Cloud Backend — Tech Stack Decisions

**Stage**: CONSTRUCTION → NFR Requirements · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25

The core stack is **fixed by tech-env.md + NFR-6** (not re-litigated). This document confirms it for U3 and records the **library-level picks** the baseline left open, each with rationale and rejected alternatives.

---

## Fixed by project baseline (confirmed for U3)

| Concern | Choice | Source |
|---|---|---|
| Language / runtime | C# 13 / .NET 10 (LTS) | tech-env, NFR-6.1 |
| API framework | ASP.NET Core Web API (REST) | tech-env, NFR-6.1 |
| ORM / DB | EF Core + **Npgsql** on **PostgreSQL** | tech-env, NFR-6.1 |
| Identity / auth | ASP.NET Core Identity + **JWT bearer** | tech-env, NFR-2.5/2.6 |
| Event log | U1 `IEventStore` → **`PostgresEventStore`** (U3-authored Npgsql adapter) | app-design, D-26 |
| Contracts / envelope | U2 `EventManager.Contracts` (`EventEnvelopeMapper`) | U2 |
| Payments | U8 `EventManager.Payments` (`IPaymentProvider` stub) | U8, D-06 |
| IDs | U1 `IIdGenerator` (Snowflake, BIGINT) | NFR-6.5 |
| Test framework | xUnit + **FsCheck** | NFR-4.2 |
| Packaging | Docker image + Docker Compose (API + PostgreSQL) | NFR-6.4 |

---

## Library-level decisions (open → decided)

### TSD-1 — PostgreSQL event-store adapter
**Decision:** U3 implements `IEventStore` as **`PostgresEventStore`** (Npgsql), backing the append-only `events` table (PK = Snowflake `EventId`; unique `(DeviceId, SequenceNumber)`; `EventScopeId` indexed for event-scoped partitioning/ingest authz). Idempotent `AppendIfNotExistsAsync` via `INSERT … ON CONFLICT DO NOTHING`.
**Rejected:** Marten/EventStoreDB — heavier, diverges from the shared `IEventStore` contract and the one-EF-pattern mandate.

### TSD-2 — TOTP / MFA
**Decision:** ASP.NET Core Identity's **built-in authenticator (RFC 6238 TOTP)** for enrollment, QR provisioning URI, and recovery codes.
**Rejected:** Otp.NET / custom — reinvents what Identity ships; more surface to secure.

### TSD-3 — Breached-password check (Q1=A)
**Decision:** **Offline bundled dataset** — a hashed (SHA-1 prefix, k-anonymity) breached-password set shipped in the image; custom `IPasswordValidator<TUser>` does a prefix lookup at register/change. No runtime external call.
**Rejected:** Live HaveIBeenPwned range API (external runtime dependency + hot-path latency, contrary to provider-agnostic posture); no-check (violates NFR-2.6).

### TSD-4 — Rate limiting (Q3=A)
**Decision:** **`Microsoft.AspNetCore.RateLimiting`** (built-in). Fixed-window policies: login 5/min per IP+account, registration 10/hour per IP; ingest exempt. 429 responses generic.
**Rejected:** AspNetCoreRateLimit (3rd-party) — built-in middleware now covers the need without an extra dependency (supply-chain, NFR-2.9).

### TSD-5 — Request validation (U3-NFR-S9)
**Decision:** **FluentValidation** for complex models (registration, bulk batch, event/division config with cross-field + range rules); DataAnnotations for simple DTOs. Validation runs **before** the event-write path.
**Rejected:** DataAnnotations-only — insufficient for cross-field/collection rules like division no-overlap and batch conflict pre-checks.

### TSD-6 — JWT issuance / refresh (Q2=A)
**Decision:** `Microsoft.AspNetCore.Authentication.JwtBearer` for validation; U3-authored token service issues access (~60 min) + rotating sliding refresh (~14 d); **refresh tokens + revocation entries persisted in PostgreSQL** (rotation-on-use, logout revokes).
**Rejected:** IdentityServer/Duende — full OAuth server is overkill for a single first-party API; OSS licensing/complexity unjustified for MVP.

### TSD-7 — Idempotency store (Q4=A)
**Decision:** Dedicated **PostgreSQL `idempotency_keys` table** (key, first-result hash, created-at; 30-day retention sweep), checked/written inside the write transaction. Serves bulk-batch keys (BR-REG-7) and payment idempotency (BR-PAY-1/3).
**Rejected:** In-memory/distributed cache — non-durable across restarts; weakens the atomic-batch guarantee.

### TSD-8 — Email delivery (functional-design Q5=A)
**Decision:** `IEmailSender` seam with a **stub/log implementation** (writes confirmation/invite tokens to structured logs / a dev-inbox table). Mirrors the D-06 payment-stub pattern; real SMTP adapter drops in later.
**Rejected:** Real SMTP now (external dependency not budgeted by tech-env); no-email (loses US-101 confirmation + US-108 invite acceptance criteria).

### TSD-9 — At-rest encryption (Q5=A)
**Decision:** **Storage/volume-level encryption** (encrypted Docker volume / provider-managed encrypted disk). Provider-agnostic, no app-layer key management in MVP.
**Rejected:** pgcrypto column-level now — added code + key-management burden; MFA secrets already Identity-encrypted; deferred post-MVP.

### TSD-10 — Health checks (U3-NFR-R6)
**Decision:** `Microsoft.Extensions.Diagnostics.HealthChecks` — shallow `/health` (liveness) + `AddNpgSql` deep DB-connectivity readiness probe.
**Rejected:** custom controller — reinvents the standard middleware.

---

## Dependency summary (new to U3, all pinned per NFR-2.9)
`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `FluentValidation.AspNetCore`, `Microsoft.Extensions.Diagnostics.HealthChecks` + `AspNetCore.HealthChecks.NpgSql`, `FsCheck.Xunit` (test). Built-in: rate limiting, Identity TOTP. Project refs: U1 `EventManager.Domain`/`EventManager.Sync`, U2 `EventManager.Contracts`, U8 `EventManager.Payments`.
