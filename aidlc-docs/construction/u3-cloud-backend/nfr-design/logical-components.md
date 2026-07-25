# U3 Cloud Backend — Logical Components

**Stage**: CONSTRUCTION → NFR Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Logical (technology-shaped but pre-code) component set and wiring. Realizes the patterns in `nfr-design-patterns.md`; feeds Code Generation.

---

## 1. Component map (layers)

```
HTTP pipeline (middleware)
  ├─ Exception handler (SP-7)  ├─ Security headers  ├─ HTTPS/HSTS
  ├─ Rate limiter (SP-4)       ├─ Authn (JwtBearer, SP-1)
  └─ Authz filter (SP-2, deny-by-default)

Controllers (thin)
  AccountController · EventController · OrganizerController
  RegistrationController · EventIngestController · ResultsController
        │ (validated request models — FluentValidation gate, SP-3)
        ▼
Application services (orchestration — S-1/S-2/S-7)
  RegistrationService · OrganizerRoleService · EventService
  DivisionService · IngestService · ResultsQueryService
        │  append events / read projections / call idempotency + external seams
        ▼
Domain (U1, reused)            Persistence & infra (U3-authored)
  RoleAuthorizationPolicy        PostgresEventStore : IEventStore  (RP-1)
  DivisionAssignment/Seeding     CloudProjectionHost + projections (PP-2)
  WeighInPolicyEvaluator         IdempotencyStore                  (RP-2)
  entities/value objects         TokenService + RefreshTokenStore  (SP-1)
                                 BreachedPasswordValidator         (SP-5)
                                 IEmailSender (stub)               (TSD-8)
Contracts (U2): EventEnvelopeMapper · validators
Payments (U8): IPaymentProvider (stub)
```

---

## 2. Components

### 2.1 Persistence
| Component | Responsibility | Realizes |
|---|---|---|
| **`PostgresEventStore : IEventStore`** | Npgsql adapter over the append-only `events` table; idempotent `AppendIfNotExistsAsync` (`ON CONFLICT DO NOTHING`), `ReadStreamAsync`, `HighWaterMarkAsync`, `ReadAllAsync`, `ListDeviceIdsAsync`. PK `EventId`; unique `(DeviceId, SequenceNumber)`; `EventScopeId` indexed. | RP-1, SC-1 |
| **`IdempotencyStore`** | `idempotency_keys` table access; check-and-record within the command transaction; retention sweep. | RP-2 |
| **`RefreshTokenStore`** | Persist/rotate/revoke refresh tokens; revocation lookup on refresh. | SP-1 |
| **EF Core `AppDbContext`(s)** | Identity tables (accounts/MFA/lockout) + read-model tables + events/idempotency/token tables. Expand/contract migrations. | RP-5 |

### 2.2 Projection host
| Component | Responsibility | Realizes |
|---|---|---|
| **`CloudProjectionHost`** | `RebuildAsync()` on startup (fold log → read models) + `Dispatch(evt)` inline after append. Registers the projections below. | PP-2 |
| **Projections** (`IProjection<TState>`) | `EventProjection`, `DivisionProjection`, `RosterProjection`, `OrganizerProjection`, `ResultsProjection` — each folds its event subset into read-model tables. `ResultsProjection` ignores unknown ingested types (BR-ING-3). | PP-1 |

### 2.3 Application services (orchestration)
| Service | Owns (stories) | Key collaborators |
|---|---|---|
| **`EventService`** | event create/edit, division config, payment options, weigh-in policy (US-104/105/106/107) | validators, `IEventStore`, projections |
| **`OrganizerRoleService`** | add/invite organizer, role change, last-admin guard (US-108/109) | `RoleAuthorizationPolicy` (U1), `IEmailSender`, `IEventStore` |
| **`RegistrationService`** | self/parent/bulk registration, edits/withdraw, roster mgmt, division assignment (US-201–207/209–211) | U1 assignment logic, `IdempotencyStore`, `IPaymentProvider` (U8), `IEventStore` |
| **`DivisionService`** | eligibility computation shared routine (Q3=A functional) | U1 `DivisionCriteria` matching |
| **`IngestService`** | ordered idempotent ingest of replicated batches (US-504) | event-scoped authz, `IEventStore`, projection dispatch |
| **`ResultsQueryService`** | athlete results/history read (US-603) | `ResultsProjection`, object-level authz |

### 2.4 Security & cross-cutting
| Component | Responsibility | Realizes |
|---|---|---|
| **`TokenService`** | Issue access/refresh, rotation, revoke-on-logout, MFA step-up. | SP-1 |
| **Authz filter/handler** | Resolve role assignment for the target event, invoke `RoleAuthorizationPolicy`, enforce object ownership; log denials. | SP-2 |
| **`BreachedPasswordValidator : IPasswordValidator`** | Offline k-anonymity lookup against bundled dataset. | SP-5 |
| **Request validators** (FluentValidation) | Per request model, run before write path. | SP-3 |
| **Rate-limit policies** | login / registration policies; ingest exempt. | SP-4 |
| **Global exception handler + security headers + health checks** | fail-closed problem-details; `/health` shallow + deep DB probe. | SP-7, RP-6 |
| **`IEmailSender` (stub)** | Log/dev-inbox confirmation + invite tokens. | TSD-8 |

### 2.5 Consumed (already built)
| From | Component |
|---|---|
| U1 `EventManager.Domain` | `RoleAuthorizationPolicy`, division-assignment/seeding, entities/value objects |
| U1 `EventManager.Sync` | `IEventStore`, `TournamentEvent`, `IProjection<TState>`, `IIdGenerator` |
| U2 `EventManager.Contracts` | `EventEnvelopeMapper`, DTO validators |
| U8 `EventManager.Payments` | `IPaymentProvider` (`StubPaymentProvider`) |

---

## 3. Wiring into the S-1 / S-2 / S-7 flows

- **S-1 Registration (write path):** `RegistrationController` → FluentValidation → `RegistrationService` → `DivisionService.ComputeEligible` (U1) → `IdempotencyStore` (batch/payment key) → `IPaymentProvider` (U8, card path) → `PostgresEventStore.AppendIfNotExists` (atomic multi-event, RP-4) → `CloudProjectionHost.Dispatch` (RosterProjection) → response.
- **S-2 Organizer & RBAC:** `OrganizerController` → Authz filter (`RoleAuthorizationPolicy`) → `OrganizerRoleService` (last-admin guard) → append RBAC events → `OrganizerProjection`. Same policy instance the hub uses.
- **S-7 Replication ingest:** `EventIngestController` → event-scoped authz (SP-6) → `IngestService` orders by `SequenceNumber` → `AppendIfNotExists` (idempotent) → inline projection dispatch (incl. `ResultsProjection`) → `IngestResult{accepted, duplicatesSkipped, highWaterMark}`.

## 4. Deployment shape (logical; detailed in Infra Design)
Single API container + PostgreSQL via Docker Compose (SC-1); encrypted volume (Q5); env-injected secrets; `/health` for the container orchestrator.

## 5. Component → pattern/requirement traceability
`PostgresEventStore`→RP-1/SC-1/R3 · `IdempotencyStore`→RP-2/R4 · `CloudProjectionHost`+projections→PP-1/PP-2 · `TokenService`+`RefreshTokenStore`→SP-1/S7 · Authz filter→SP-2/S3 · `BreachedPasswordValidator`→SP-5/S4 · validators→SP-3/S9 · rate-limit→SP-4/S8 · exception+headers+health→SP-7/RP-6 · `IEmailSender`→TSD-8 · `IngestService`→SP-6/RP-1.
