# U3 Cloud Backend — Code Generation Summary

**Stage**: CONSTRUCTION → Code Generation · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Greenfield ASP.NET Core Web API added to `backend/`. **Builds green; 20 tests passing.**

All application code is under the workspace root (`backend/`); this file is the markdown summary only.

## Projects
- **`backend/EventManager.Api/`** — the cloud backend Web API (.NET 10). References U1 `EventManager.Domain`/`EventManager.Sync`, U2 `EventManager.Contracts`, U8 `EventManager.Payments`.
- **`backend/tests/EventManager.Api.Tests/`** — xUnit + FsCheck (SQLite in-memory harness). 20 tests.
- Both added to `backend/EventManager.Backend.slnx`.

## Business-logic layer (Services/)
| File | Responsibility | Stories |
|---|---|---|
| `AccountService.cs` | register (breach check + email-confirm gate), login (lockout + MFA), TOTP enroll | US-101/102/103 |
| `TokenService.cs` | JWT issue + rotating refresh + revocation (Q2=A) | US-102 |
| `Security.cs` | `BreachedPasswordValidator` (offline, SP-5), `OutboxEmailSender` stub (Q5), `OutboundRetry` (Q3=A) | US-101/108 |
| `EventService.cs` | event create/edit, divisions (no-overlap), payment options, weigh-in policy | US-104/105/106/107 |
| `DivisionEligibility.cs` | pure deterministic eligibility matching (PBT-1) | US-202/210 |
| `RegistrationService.cs` | profiles, self/parent/bulk-atomic registration, edits, roster mgmt, payment via U8 | US-201–207/209–211 |
| `OrganizerRoleService.cs` | add/invite organizer, role change, last-admin guard (U1 policy) | US-108/109 |
| `IngestService.cs` | event-scoped idempotent replication ingest | US-504 |
| `ResultsQueryService.cs` | athlete results/history read | US-603 |

## Repository / persistence layer (Persistence/, Events/)
`AppDbContext` (Identity + event log + read models + infra tables), `PostgresEventStore : IEventStore` (provider-agnostic idempotent append), `IdempotencyStore`, `RefreshTokenStore`, `EventWriter` (single write path: mint→append→project), `Events/EventPayloads.cs` (event vocabulary), `Projections/CloudProjectionHost.cs` (5 projections, synchronous inline). `Persistence/Migrations/InitialCreate` (EF Core, expand phase).

## API layer (Controllers/, Contracts/, Validation/, Auth/)
6 controllers (`Account`, `Event`, `Organizer`, `Registration`, `EventIngest`, `Results`) on the ErrorOr→HTTP base; `ApiContracts.cs` DTOs; `Validators.cs` (FluentValidation, runs before the write path); `Auth/EventAuthorizer.cs` (deny-by-default RBAC over the U1 policy) + `CurrentUser`. `Program.cs` wires DI, JWT auth, rate limiting (login 5/min, registration 10/hr), health checks (`/health`, `/health/ready`), exception handler, security headers.

## Deployment artifacts (backend/)
`EventManager.Api/Dockerfile` (multi-stage, pinned, non-root), `docker-compose.yml` (proxy/api/db/backup), `Caddyfile`, `backup/backup.sh`, `.env.example`; `.github/workflows/backend.yml` (CI).

## Story → code traceability
US-101→AccountController.Register+BreachedPasswordValidator · US-102→Login+TokenService · US-103→mfa/enroll,confirm · US-104→EventController.Create (+auto Full Admin) · US-105→WeighInPolicy · US-106→ConfigureDivision (overlap guard) · US-107→PaymentOptions · US-108→Organizer.Add (existing/invite) · US-109→ChangeRole/Remove (last-admin guard) · US-201/203→UpsertProfile · US-202/207/210→Register+DivisionEligibility · US-205/206→RegisterBatch (atomic+idempotent) · US-209→SetPaymentStatus/roster · US-211→Edit/Withdraw · US-504→EventIngestController · US-603→ResultsController.

## Test summary (20 passing)
- **PBT-1** eligibility determinism + order-independence (`DivisionEligibilityTests`).
- **PBT-2** no double-registration; atomic bulk; idempotent resubmit; payment decline→Owed; window-closed reject (`RegistrationServiceTests`).
- **PBT-3** RBAC deny-by-default; co-organizer blocked from Full-Admin-only; last-admin guard (`RbacTests`).
- **PBT-4** ingest idempotency; unauthorized-scope reject; order-independent fold (`IngestServiceTests`).
- Breached-password accept/reject (`SecurityTests`).

## Documented seams (not built — per NFR/Infra decisions)
Real SMTP (Q5), live payment provider + circuit breaker (Q3/D-06), column-level encryption (Q5), async projector (Q2), multi-instance API (Q1). All isolated behind interfaces so they drop in without touching consumers.

## Verification
`dotnet build backend/EventManager.Backend.slnx` → **Build succeeded**. `dotnet test …EventManager.Api.Tests` → **20 passed**. Full execution (against PostgreSQL, container up) is exercised in the Build & Test phase.
