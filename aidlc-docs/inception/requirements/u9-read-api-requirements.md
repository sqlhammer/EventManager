# Requirements — Unit U9: Read/Query API

**Stage**: INCEPTION → Requirements Analysis
**Depth**: Standard
**Created**: 2026-07-26
**Status**: Awaiting approval

---

## 1. Intent Analysis

| Aspect | Assessment |
|---|---|
| **User request** | "Create GET endpoints for: event (single and all that the user has access to), division (single and all for the event), weigh-in policy (single and all for the event), registrant (single and all for the event), account (single and all with roles for the event)" |
| **Request type** | New Feature — a read/query surface on the existing cloud backend |
| **Scope estimate** | Single Component — `backend/EventManager.Api`, plus a possible shared-library touch for the RBAC read action (see U9-CON-1) |
| **Complexity estimate** | Moderate — no new domain logic or persistence, but a genuinely new three-tier authorization model that does not exist today |
| **Clarity** | Clear after two clarification rounds (10 + 3 questions) |

### Why this is only Moderate, not Complex
The read models these endpoints serve **already exist and are already populated**. `EventRow`,
`DivisionRow`, `RegistrationRow`, `OrganizerRow`, and `AthleteProfileRow`
([Entities.cs](../../../backend/EventManager.Api/Persistence/Entities.cs)) are folded from the event
log by [CloudProjectionHost](../../../backend/EventManager.Api/Projections/CloudProjectionHost.cs).
No new events, projections, migrations, or write paths are required. The substance of this unit is
**authorization and response shaping**, not data.

### Baseline: what exists today
`backend/EventManager.Api` is write-only except for a single GET —
[ResultsController.cs:13](../../../backend/EventManager.Api/Controllers/ResultsController.cs#L13)
(`GET /api/results/athletes/{athleteId}`). Every other controller exposes POST/PUT/DELETE only.

---

## 2. Decision Record (traceability)

| Ref | Question | Answer | Effect |
|---|---|---|---|
| D-U9-01 | Primary consumer | Q1=**D** — no specific client yet; build for API completeness | No client-driven shaping; weakens the caching case (see D-U9-09) |
| D-U9-02 | Meaning of "events I have access to" | Q2=**C**, revised by C1=**C** | Collection spans three access tiers |
| D-U9-03 | Read authorization model | Q3=A, **superseded by C1=C** | **Three-tier reads** (public / registrant / organizer) |
| D-U9-04 | Weigh-in policy shape | Q4=**A**, narrowed 2026-07-26 | Read-only over today's model; **single endpoint only** — the one-item collection form was removed from scope at user request; no data-model change |
| D-U9-05 | Account endpoint scope | Q5=C, **superseded by C2=B** | **Organizer roster only** |
| D-U9-06 | Registrant PII exposure | Q6=**B** | Minimal on list; full detail on single-item endpoint |
| D-U9-07 | Pagination / filtering | Q7=**A** | No pagination, no general filtering or sorting |
| D-U9-08 | Withdrawn / completed / deleted rows | Q8=**A** | Excluded by default; opt in via inclusion flag |
| D-U9-09 | HTTP caching | Q9=X → C3=**D** | **Watermark ETags** on event-scoped endpoints; none on the cross-scope event list |
| D-U9-10 | Extensions | Q10=**A** | Security Baseline, PBT, and Resiliency all carry over, Full/blocking |

Two answers were superseded during clarification because they were mutually inconsistent — see
[u9-read-api-clarification-questions.md](u9-read-api-clarification-questions.md). Q3=A (organizers
only) conflicted with Q2=C (public discovery) and would have made `POST /api/registration`
unreachable for registrants, since division ids could never be discovered. Q5=C (any account by id)
was a blocking SECURITY-08 finding.

---

## 3. Access Tier Model (core of this unit)

All endpoints require authentication — there is **no anonymous access**. "Public" below means *any
authenticated caller*.

| Tier | Caller qualifies when | Grants |
|---|---|---|
| **T0 — Public** | Any authenticated account, for an event whose `RegistrationStatus == Open` | Event **summary**, divisions, weigh-in policy |
| **T1 — Registrant** | Caller has a non-withdrawn `RegistrationRow` in the event (`ManagedByAccountId == caller`) | Everything in T0, plus event **detail**, plus their **own** registrations |
| **T2 — Organizer** | Caller holds an `OrganizerRow` for the event (Full Admin or Co-Organizer) | Everything in T1, plus the **full registrant roster** and the **organizer/account roster** |

Tiers are cumulative and evaluated per event. Deny-by-default: a caller matching no tier is treated
as having no access to that event.

### Response shapes
- **Event summary** (T0): name, venue, date, registration window, entry fee, registration status
- **Event detail** (T1/T2): summary + card-enabled, check-in-started, weigh-in policy, created-by
- **Registrant list item** (T2): athlete name, academy, division ids, payment status, mismatch flag
- **Registrant detail** (T1 own / T2 any): list item + date of birth, weight, rank, gender

---

## 4. Endpoint Inventory

| # | Endpoint | Min. tier | Notes |
|---|---|---|---|
| 1 | `GET /api/events` | — | Union across tiers; each item tagged with the caller's `accessTier` and organizer `role` where applicable. Summary shape. **No ETag.** |
| 2 | `GET /api/events/{eventId}` | T0 | Summary at T0; detail at T1/T2 |
| 3 | `GET /api/events/{eventId}/divisions` | T0 | `?includeCompleted=true` to include `Status == Complete` |
| 4 | `GET /api/events/{eventId}/divisions/{divisionId}` | T0 | |
| 5 | `GET /api/events/{eventId}/weigh-in-policy` | T0 | The single effective policy. There is no collection form — an event has exactly one policy, so a one-item collection would carry no information the single endpoint does not |
| 6 | `GET /api/events/{eventId}/registrants` | T2 | List shape; `?includeWithdrawn=true` |
| 7 | `GET /api/events/{eventId}/registrants/{registrationId}` | T1 (own) / T2 (any) | Detail shape |
| 8 | `GET /api/events/{eventId}/accounts` | T2 | Organizer roster with roles |
| 9 | `GET /api/events/{eventId}/accounts/{accountId}` | T2 | 404 if that account holds no organizer role on the event |

Division and account endpoints are **event-scoped by path** rather than top-level, so that every
resource id is authorized against an event the caller demonstrably has a tier on. This is the
structural defence against IDOR (SECURITY-08).

---

## 5. Functional Requirements

| ID | Requirement | Source |
|---|---|---|
| **U9-FR-1** | Provide the nine GET endpoints in §4 on `backend/EventManager.Api`. | User request |
| **U9-FR-2** | Implement the three-tier access model in §3, evaluated per event, deny-by-default. | C1=C |
| **U9-FR-3** | `GET /api/events` returns the union of T0, T1, and T2 events for the caller, each tagged with the caller's effective tier and organizer role where held. | Q2=C, C1=C |
| **U9-FR-4** | Every single-item endpoint verifies the caller's tier on the owning event **before** the resource is returned; a resource whose id does not belong to the path event is treated as not found. | SECURITY-08 |
| **U9-FR-5** | Registrant list returns the minimal shape; registrant detail returns the full shape including profile fields. | Q6=B |
| **U9-FR-6** | A T1 caller may read a registration detail record only where `ManagedByAccountId == caller`. | Q6=B + SECURITY-08 |
| **U9-FR-7** | Account endpoints expose only accounts holding an organizer role on the path event, each with its `OrganizerRole`. | C2=B |
| **U9-FR-8** | Weigh-in policy is served read-only from `EventRow.WeighInPolicyMode` / `WeighInTolerancePercent` through a single endpoint; no collection form, no new entity, event, or migration. | Q4=A, narrowed |
| **U9-FR-9** | Collections return the complete result set — no pagination, no general-purpose filtering or sorting. | Q7=A |
| **U9-FR-10** | Withdrawn registrations and completed divisions are excluded by default and included only via an explicit inclusion flag. ~~and soft-deleted accounts~~ — **amended 2026-07-26 (Functional Design Q3=A)**: the soft-deleted-accounts clause was inert and is withdrawn. Account deletion appends `OrganizerRemoved` for every role held and the projection deletes the `OrganizerRow`, so a deleted account can never appear in an organizer roster and the flag would have nothing to include. See BR-READ-17. | Q8=A, amended |
| **U9-FR-11** | Event-scoped endpoints (2–9) emit a strong `ETag` and honour `If-None-Match` with `304 Not Modified`, subject to U9-CON-2. | C3=D |
| **U9-FR-12** | Absent, unauthorized, and non-existent resources are indistinguishable to callers without a tier — no existence disclosure through status-code differences. | SECURITY-08 |

---

## 6. Non-Functional Requirements

| ID | Requirement | Source |
|---|---|---|
| **U9-NFR-1** | ETag derivation: `MAX(EventRecord.EventId) WHERE EventScopeId = {eventId}`, served via the existing index at [AppDbContext.cs:36](../../../backend/EventManager.Api/Persistence/AppDbContext.cs#L36). A conditional hit must answer 304 **without querying read-model tables**. | C3=D |
| **U9-NFR-2** | All read endpoints are `[Authorize]`; no `[AllowAnonymous]` on any endpoint in this unit. | SECURITY-08 |
| **U9-NFR-3** | Every query is parameterized through EF Core; no raw SQL string composition. | SECURITY-05 |
| **U9-NFR-4** | Path and query parameters are validated (type, bounds, allowed flag values) before any data access. | SECURITY-05 |
| **U9-NFR-5** | Authorization denials are logged with actor, event id, and endpoint, without logging PII. | SECURITY-03, SECURITY-14 |
| **U9-NFR-6** | Read endpoints add no new infrastructure — same container, database, and deployment as U3. Inherits U3's targets: criticality Medium, 99.5% availability, RTO ≤ 4h, RPO ≤ 24h. | U3-NFR-R1/R2 |
| **U9-NFR-7** | Errors fail closed: any failure in tier resolution denies access rather than falling through to a permissive default. | SECURITY-15 |
| **U9-NFR-8** | Coding standard CS-1 applies — no ternary `?:` operators. | `aidlc-docs/coding-standards.md` |
| **U9-NFR-9** | No N+1 query patterns; each collection endpoint resolves in a bounded number of round trips independent of result size. | Performance |

---

## 7. Constraints and Open Design Decisions

These are identified now so they are resolved in design rather than discovered during code generation.

### U9-CON-1 — The RBAC model has no read action — ✅ RESOLVED 2026-07-26
**Decision (user)**: use an **API-local read authorizer**. The shared `OrganizerAction` enum is
**not** extended, `shared/EventManager.Domain` is **not** modified, and `admin/EventManager.Hub`
is unaffected. The blast radius stays inside `backend/EventManager.Api` and its test assembly.
The original analysis follows for the record.

[EventAuthorizer](../../../backend/EventManager.Api/Auth/EventAuthorizer.cs) delegates to the shared
U1 `RoleAuthorizationPolicy`, whose `OrganizerAction` enum
([Enums.cs:23](../../../shared/EventManager.Domain/Enums.cs#L23)) contains only organizer **write**
actions. The three-tier model needs a read concept that also covers non-organizers, which
`OrganizerAction` cannot express at all.

Adding members to `OrganizerAction` changes `shared/EventManager.Domain`, which the hub also consumes
via `OfflineOrganizerAuth` — so it is a cross-unit change affecting U4a. **Functional Design must
choose** between extending the shared enum and introducing a read-authorization component local to
the API. The second option is likely correct, since tiers T0 and T1 are not organizer roles at all,
but this is a design decision, not a requirements one.

### U9-CON-2 — Watermark ETags do not cover athlete profile changes
The watermark in U9-NFR-1 is sound for event-scoped data because
[CloudProjectionHost](../../../backend/EventManager.Api/Projections/CloudProjectionHost.cs) is a
**synchronous inline** projection host — read models are folded in the same transaction and
`DbContext` as the event append ([EventWriter.cs:33-36](../../../backend/EventManager.Api/Events/EventWriter.cs#L33-L36)),
so there is no projection lag and the log watermark is an exact version token for the read models.

**But athlete profile events are not event-scoped.** `UpsertProfileAsync` appends
`AthleteProfileCreated`/`Updated` with the **athlete id** as the scope
([RegistrationService.cs:43-44](../../../backend/EventManager.Api/Services/RegistrationService.cs#L43-L44)),
not a tournament event id. Registrant **detail** (endpoint 7) includes profile fields — date of
birth, weight, rank, gender — sourced from `AthleteProfileRow`. So if an athlete updates their
weight, the event watermark does not move and a cached client would receive a **304 with stale
data**. This is a correctness defect, not a performance one.

Functional Design must resolve it one of two ways:
- **(a)** exclude endpoint 7 from ETag coverage — simplest, small loss; or
- **(b)** compose the watermark as the max across the event scope and the scopes of the athletes
  referenced by the response — correct but more expensive and more to test.

### U9-CON-3 — Watermark validity depends on inline projection
U9-NFR-1 is only correct while projection remains synchronous and inline. If projections ever become
asynchronous — a plausible scaling change — the ETag must switch to a projection-applied high-water
mark. This constraint must be recorded in the code so a future change does not silently break
caching correctness.

### U9-CON-4 — Assumption: weigh-in policy is T0
Not directly asked. Weigh-in policy is placed in the **public tier** alongside divisions because the
tolerance percent materially affects a prospective registrant's division choice — withholding it
until after registration would be user-hostile. **Override this at approval if you disagree**;
moving it to T1 is a one-line change to the tier table.

### U9-CON-5 — Assumption: registrant self-service reads are in scope, roster reads are not
U9-FR-6 lets a T1 caller read their own registration. This is the minimum that makes the registrant
tier meaningful, and it reuses the ownership check already present in
[ResultsQueryService.cs:18](../../../backend/EventManager.Api/Services/ResultsQueryService.cs#L18).
A registrant still cannot list other registrants.

---

## 8. Out of Scope

- Any write, update, or delete endpoint
- The Blazor web portal (`EventManager.Web`) or any other client — Q1=D, no consumer exists yet
- Per-division weigh-in policy overrides — explicitly declined at Q4=A
- A weigh-in-policy **collection** endpoint (`GET /api/events/{eventId}/weigh-in-policies`) — removed from scope 2026-07-26. If per-division overrides are ever added, a collection form becomes meaningful and can be introduced then
- Bracket, match, mat, and schedule read endpoints — not requested
- Pagination, sorting, and general query filtering — declined at Q7=A
- Changes to the event log, projections, or database schema

---

## 9. Extension Compliance (Requirements stage)

All three extensions are enabled Full/blocking per D-U9-10.

### Security Baseline
| Rule | Status | Note |
|---|---|---|
| SECURITY-01 Encryption | N/A this stage | Inherited from U3 infrastructure; no new stores |
| SECURITY-02 Intermediary logging | N/A this stage | Inherited from U3 (Caddy access logs) |
| SECURITY-03 App logging | Compliant | U9-NFR-5 |
| SECURITY-04 HTTP headers | N/A | No HTML-serving endpoints; JSON API only |
| SECURITY-05 Input validation | Compliant | U9-NFR-3, U9-NFR-4 |
| SECURITY-06 Least privilege | N/A this stage | No new IAM/infra policies |
| SECURITY-07 Network config | N/A this stage | No network change |
| SECURITY-08 Access control | **Compliant — was the key finding** | Q5=C rejected as an enumeration/IDOR vector; resolved to C2=B. U9-FR-4, U9-FR-6, U9-FR-12, U9-NFR-2 |
| SECURITY-09 Hardening | N/A this stage | Inherited from U3 |
| SECURITY-10 Supply chain | N/A this stage | No new dependencies anticipated |
| SECURITY-11 Secure design | Compliant | Tier resolution isolated in one component (U9-CON-1); abuse case addressed: account enumeration |
| SECURITY-12 AuthN | N/A | No change to authentication |
| SECURITY-13 Integrity | N/A | No deserialization of untrusted input |
| SECURITY-14 Alerting | Compliant | U9-NFR-5 — authorization failures logged for alerting |
| SECURITY-15 Exception handling | Compliant | U9-NFR-7 fail-closed |

### Property-Based Testing
| Rule | Status | Note |
|---|---|---|
| PBT-01 Property identification | **Deferred to Functional Design** — required there, not here | Candidate properties already visible: *(invariant)* no caller without a tier ever receives event data, for all generated caller/event pairs; *(invariant)* a T0 response never contains a field outside the summary shape; *(oracle)* query results match a naive in-memory filter over the same rows; *(idempotence)* repeated GETs with an unchanged watermark return byte-identical bodies |
| PBT-09 Framework | Compliant | FsCheck already in use in `backend/tests/EventManager.Api.Tests` |
| PBT-02..08, 10 | Deferred to Code Generation | Per the extension's own stage mapping |

### Resiliency Baseline
| Rule | Status | Note |
|---|---|---|
| RESILIENCY-01, 02 | Compliant by inheritance | U3-NFR-R1/R2 — Medium criticality, 99.5%, RTO ≤ 4h, RPO ≤ 24h. **Not re-asked**: this unit adds no workload, and the extension directs conformance to an existing decision rather than inventing a new one |
| RESILIENCY-03, 04 | Compliant by inheritance | U3 deployment/rollback flow (`deployment-architecture.md` §4) |
| RESILIENCY-05, 06, 07 | Compliant by inheritance | Existing health checks and logging; no new component |
| RESILIENCY-08..13 | N/A this unit | No new compute, data store, or DR surface |
| RESILIENCY-14, 15 | N/A this unit | Inherited from U3 |

**No blocking findings at this stage.**

---

## 10. Traceability

| Requirement | Existing story / persona |
|---|---|
| U9-FR-3 (T2 events) | US-104..107 organizer event setup (P1 Organizer) |
| U9-FR-3 (T0 discovery) | US-201..203 registrant self-registration (P3 Registrant) |
| U9-FR-5, U9-FR-6 | US-207, US-211 registration view/edit (P3 Registrant, P2 Coach) |
| U9-FR-7 | US-108, US-109 organizer RBAC management (P1 Organizer) |
| U9-FR-8 | US-308, US-309 weigh-in policy (P1, P5) |

No new personas are required — T0/T1/T2 map onto the existing P1 Organizer, P2 Coach, and
P3 Registrant personas in [personas.md](../user-stories/personas.md).

**New user stories are warranted** for the tiered read behaviour: it is user-facing, spans three
personas, and has acceptance criteria per tier that are worth pinning explicitly. Recommended as the
next stage.
