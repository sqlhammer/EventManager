# U3 Cloud Backend — Code Generation Plan

**Stage**: CONSTRUCTION → Code Generation (per-unit) · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Single source of truth for U3 generation. User pre-authorized continuing from plan → generation without a separate gate.

## Unit context
- **Stories (20)**: US-101–109, US-201–207, US-209–211, US-603.
- **Depends on (project references)**: U1 `EventManager.Domain` + `EventManager.Sync`; U2 `EventManager.Contracts`; U8 `EventManager.Payments`.
- **Owns entities**: Account (Identity), Event/Division/Registration/OrganizerRoleAssignment/AthleteProfile projections, PaymentStatus, ResultsProjection, idempotency keys, refresh tokens (per `functional-design/domain-entities.md`).
- **Code location**: `backend/EventManager.Api/` (app), `backend/tests/EventManager.Api.Tests/` (tests), `backend/` (infra files). Extends the existing `backend/` solution (U8 Payments already there). Docs → `aidlc-docs/construction/u3-cloud-backend/code/`.

## Design inputs
Functional: `functional-design/*`. NFR: patterns SP/PP/RP/SC/OB, TSD-1..10. Infra: Compose (proxy/api/db/backup). Decisions: Q1=C persistence hybrid, Q2=A bulk atomic, Q3=A eligibility+confirm, Q4=A edits-as-events, Q5=A email stub, Q6=A results projection, Q7=A event-scoped ingest; NFR Q1=A single instance, Q2=A synchronous projections, Q3=A retry-no-breaker; Infra Q1–Q5=A.

---

## Steps

### Step 1 — Project setup
- [x] `backend/EventManager.Api/EventManager.Api.csproj` (Sdk.Web, net10.0) — packages: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.Design, Microsoft.AspNetCore.Identity.EntityFrameworkCore, Microsoft.AspNetCore.Authentication.JwtBearer, FluentValidation.DependencyInjectionExtensions; project refs to Domain/Sync/Contracts/Payments. Verify `dotnet restore`.
- [x] Add API + test projects to `backend/EventManager.Backend.slnx`.

### Step 2 — Persistence / repository layer  (`Persistence/`)  [BR-X-1/2, RP-1/2, SP-1]
- [x] `Events/EventRecord.cs` — EF entity mirroring `TournamentEvent` (BIGINT PK EventId, unique (DeviceId,SequenceNumber), EventScopeId index, payload bytea).
- [x] `AppDbContext.cs` — `IdentityDbContext<AppUser>` + DbSets: Events, read-model tables (EventRow, DivisionRow, RegistrationRow, OrganizerRow, ResultRow, AthleteProfileRow, AthleteOwnership), IdempotencyKeys, RefreshTokens, EmailOutbox; constraints/indexes via `OnModelCreating`.
- [x] `PostgresEventStore.cs : IEventStore` — `AppendIfNotExistsAsync` (ON CONFLICT DO NOTHING), ReadStream/HighWaterMark/ReadAll/ListDeviceIds.
- [x] `IdempotencyStore.cs` — check/record within transaction.
- [x] `RefreshTokenStore.cs` — issue/rotate/revoke lookup.
- [x] `AppUser.cs` — `IdentityUser<long>` + Snowflake account id.
- [x] Repository unit tests (in-memory/SQLite provider) + summary.

### Step 3 — Event payloads & serialization  (`Events/`)  [Q1=C]
- [x] `EventTypes.cs` + payload records for the U3 vocabulary (EventCreated, EventDetailsChanged, DivisionConfigured, RegistrationSubmitted, …). Serialize via U1 `JsonEventSerializer`; `EventFactory` builds `TournamentEvent` (Snowflake id via U1 `IIdGenerator`, cloud DeviceId, EventScopeId = tournament event id).

### Step 4 — Projections  (`Projections/`)  [PP-1/2, Q6=A]
- [x] `IReadModelProjection` + `CloudProjectionHost` (RebuildAsync + Dispatch, synchronous).
- [x] EventProjection, DivisionProjection, RosterProjection, OrganizerProjection, ResultsProjection (folds ingested result events; ignores unknown, BR-ING-3).

### Step 5 — Application services  (`Services/`)  [S-1/S-2/S-7]
- [x] `EventService` (US-104/105/106/107), `DivisionService` eligibility (US-210, Q3), `RegistrationService` (US-201–207/209–211, bulk atomic Q2), `OrganizerRoleService` (US-108/109, last-admin guard, reuse U1 RoleAuthorizationPolicy), `IngestService` (US-504, event-scoped, idempotent), `ResultsQueryService` (US-603).
- [x] `TokenService`+refresh/revocation (SP-1), `BreachedPasswordValidator` (SP-5, offline set), `IEmailSender`+`LoggingEmailSender` stub (Q5), outbound retry helper (RP-3).
- [x] Service unit tests + summary.

### Step 6 — API layer  (`Controllers/`, `Contracts/`, `Validation/`, `Auth/`)  [SP-2/3/4/6/7]
- [x] Request/response DTOs; FluentValidation validators.
- [x] Controllers: Account (US-101/102/103), Event (US-104–107), Organizer (US-108/109), Registration (US-201–207/209–211), EventIngest (US-504), Results (US-603).
- [x] Auth: JWT setup, `EventRbacRequirement`/handler over U1 policy, object-ownership checks; rate-limit policies; global exception handler; security headers; `/health` + `/health/ready`; `/metrics` seam.
- [x] `Program.cs` DI wiring; `appsettings.json`.
- [x] API unit tests + summary.

### Step 7 — Migration  (`Persistence/Migrations/`)  [RP-5]
- [x] Initial EF Core migration (expand phase) or documented `dotnet ef migrations add` note if the tool is unavailable; startup applies migrations in Development only.

### Step 8 — Tests (PBT)  (`backend/tests/EventManager.Api.Tests/`)  [NFR-4.2/4.3]
- [x] FsCheck generators + PBT-1 assignment determinism, PBT-2 no double-registration, PBT-3 RBAC deny-by-default, PBT-4 ingest idempotency; example tests (bulk conflict, last-admin, payment decline→Owed, email-confirm gate).

### Step 9 — Deployment artifacts  (`backend/`)  [Infra]
- [x] `EventManager.Api/Dockerfile` (multi-stage, pinned, non-root), `docker-compose.yml` (+ dev override), `Caddyfile`, `backup/backup.sh`, `.env.example`, `.github/workflows/backend.yml`.

### Step 10 — Build & docs
- [x] `dotnet build backend/EventManager.Backend.slnx` → green; fix compile errors in place.
- [x] Code summaries in `aidlc-docs/construction/u3-cloud-backend/code/` (business-logic, api, repository, deployment).
- [x] Update aidlc-state.md + audit.md; present completion message.

## Story traceability
US-101/102/103→AccountController+TokenService+BreachedPasswordValidator+MFA · US-104–107→EventService/EventController · US-108/109→OrganizerRoleService/OrganizerController · US-201/203→profiles · US-202/207/210/211→RegistrationService/RegistrationController · US-204→roster · US-205/206→bulk atomic · US-209→roster mgmt · US-504(ingest)→IngestService/EventIngestController · US-603→ResultsQueryService/ResultsController.

## Scope note
MVP-coherent, compiling vertical implementation. Email/payment are stubs (D-06/Q5). Card charge path delegates to U8. Real SMTP, column-level encryption, async projector, circuit breakers, multi-instance are documented seams, not built (per NFR/Infra decisions).
