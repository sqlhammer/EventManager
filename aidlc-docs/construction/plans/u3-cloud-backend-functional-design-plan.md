# U3 Cloud Backend — Functional Design Plan

**Stage**: CONSTRUCTION → Functional Design (per-unit)
**Unit**: U3 Cloud Backend (`backend/`)
**Date**: 2026-07-25
**Mode**: Stage-by-stage (not fast-tracked — large unit)

---

## Unit context (analyzed)

**Responsibility**: Pre-event accounts/auth (+MFA), organizer RBAC management, registration (self / parent / coach-bulk), division configuration + assignment, event creation, replication ingest, results/history read models. Independently deployable service; Docker + PostgreSQL; first unit with a real API layer + EF Core.

**Primary stories (20)**: US-101–109 (Epic 1 pre-event setup), US-201–207, US-209–211 (Epic 2 registration), US-603 (results & history).

**Consumes (already built & merged)**:
- **U1** `EventManager.Domain` — `EventDefinition`, `Division`, `Registration`, `AthleteProfile`, `OrganizerRoleAssignment` entities; `RoleAuthorizationPolicy` (deny-by-default RBAC); `WeighInPolicyEvaluator`; division-assignment / seeding engines.
- **U1** `EventManager.Sync` — `IEventStore` (`AppendIfNotExistsAsync`, `ReadStreamAsync`, `HighWaterMarkAsync`, `ReadAllAsync`, `ListDeviceIdsAsync`), `TournamentEvent`, `IProjection<TState>`, `IReplicationProtocol`, `IIdGenerator` / `IWorkerIdRegistry` (Snowflake).
- **U2** `EventManager.Contracts` — `EventEnvelope`, DTOs, validators, `EventEnvelopeMapper` (replication wire format).
- **U8** `EventManager.Payments` — `IPaymentProvider.ChargeAsync`, `PaymentRequest`/`PaymentResult`, `StubPaymentProvider`.

**Approved application-design orchestration** (from `services.md`): S-1 Registration, S-2 Organizer & RBAC, S-7 Replication Ingest. Controllers named in `component-methods.md`: `AccountController`, `EventController`, `OrganizerController`, `RegistrationController`, `EventIngestController`, `ResultsController`.

**Enabled extensions (blocking)**: Security Baseline, Property-Based Testing, Resiliency Baseline. Functional design must produce testable invariants (division assignment, idempotent registration, RBAC) and note security-sensitive rules (auth, enumeration, authorization) for the downstream NFR stages.

---

## Design questions (please answer inline)

> Answer each by replacing the `[Answer]:` line. Multiple-choice — pick a letter (add notes if useful). These resolve the genuine ambiguities before I write the functional-design artifacts. My recommendation is marked **(rec)**.

### Q1 — Persistence model for pre-event entities (the pivotal one)
Event-day state (scores, check-ins, weigh-ins) is unambiguously event-sourced through `IEventStore`/`TournamentEvent` and replicated from the hub. But how should **pre-event cloud-owned entities** (events, divisions, registrations, organizer-role assignments) be persisted in the cloud DB?

- **A.** Full event-sourcing — every pre-event mutation is a `TournamentEvent` appended via `PostgresEventStore.AppendIfNotExists`; read models (roster, event, divisions) are projections. Matches `services.md` S-1 ("persist as events") and "every mutation is an event." Highest fidelity, most code.
- **B.** Conventional EF Core CRUD tables for pre-event entities; the `IEventStore` log is used **only** for the replicated event-day stream ingested from the hub. Simpler; but two persistence paradigms and pre-event history isn't event-sourced.
- **C. (rec)** Hybrid: pre-event **domain** entities (event, division, registration, role assignment, weigh-in policy, payment status) are event-sourced through `IEventStore` so corrections/withdrawals are auditable events (US-104/209/211 explicitly say "recorded as events"); **ASP.NET Identity** account/credential data (US-101/102/103) stays in standard Identity EF tables (never event-sourced). Read models are projections over the domain event log.

[Answer]: C

### Q2 — Bulk-registration atomicity (US-205 vs US-206)
US-205 says a coach batch registers "atomically, one combined fee summary." US-206 says "valid entries succeed, problem entries are itemized … fix and resubmit only those." These pull in opposite directions.

- **A. (rec)** Two-phase: a **validation pass** first evaluates every athlete/division; if any conflicts (already-registered, ineligible), the batch is **rejected with an itemized report** and nothing is committed (true atomic). Coach fixes flagged entries and resubmits; idempotency key on the batch prevents double-registration on resubmit (US-206 "idempotent submission").
- **B.** Partial commit: valid entries are registered immediately, problem entries returned itemized; coach resubmits only the failures. "Atomic" interpreted per-athlete, not per-batch. One combined fee summary covers only the committed entries.
- **C.** Coach chooses per-batch: an "all-or-nothing" toggle vs "commit what's valid." More UI/flow surface.

[Answer]: A

### Q3 — Division assignment vs. self-selection (US-202 vs US-210)
US-202: the registrant **selects** divisions (only eligible ones offered). US-210: divisions are **auto-assigned** from the profile, mismatches surfaced to the organizer. How do these combine?

- **A. (rec)** Auto-compute the set of **eligible** divisions from the profile (weight/rank/age/gender via U1 assignment logic); the registrant confirms/selects from that eligible set; anything the registrant picks that later mismatches (e.g., weight changed on edit, US-211) is flagged to the organizer for override (US-210). Auto-assignment = eligibility filter + a default selection; human keeps final pick.
- **B.** Fully automatic: the system assigns all matching divisions with no registrant choice; organizer overrides mismatches. Simpler but registrant can't opt out of an eligible division.
- **C.** Fully manual: registrant picks freely from all divisions; system only warns on mismatch. Contradicts "divisions whose criteria don't match are not offered."

[Answer]: A

### Q4 — Event editing before vs. after registration opens (US-104)
US-104: organizer can edit event details "until registration opens; edits after are recorded events." Under Q1's event-sourced model, how do we treat pre-open edits?

- **A. (rec)** Uniform: **all** event mutations are events (append `EventDetailsChanged`), regardless of window state. "Before registration opens" is just a **business rule** governing which fields are editable and whether confirmation is required — not a different persistence path. Keeps one write path; simplest given Q1=A/C.
- **B.** Pre-open edits mutate the current event projection in place (no event); only post-open edits append correction events. Two paths; contradicts pure event sourcing.

[Answer]: A

### Q5 — Email delivery for confirmation & invitations (US-101, US-108)
US-101 requires email confirmation before first event creation; US-108 invites co-organizers by email. tech-env.md specifies no SMTP/email infrastructure for MVP.

- **A. (rec)** Define an `IEmailSender` seam; MVP ships a **stub/log implementation** (writes the confirmation/invite token to logs or a dev-inbox table) so the flows are exercisable end-to-end without a real provider — mirrors the D-06 payment-stub pattern. Real SMTP drops in later.
- **B.** No email at all in MVP — auto-confirm accounts and add co-organizers by direct account lookup only (drop the email-invite path). Simpler but loses US-101 confirmation and US-108 invite-by-email acceptance criteria.
- **C.** Require a real SMTP provider now (config-driven). Adds an external dependency the tech-env didn't budget for.

[Answer]: A

### Q6 — Results/history read model scope now (US-603)
Results come from event-day competition events produced by the **hub (U4b)** and replicated into U3 via ingest (S-7). Those units don't exist yet.

- **A. (rec)** Build the **projection + read API now** (`ResultsController.GetForAthlete` over a results projection folded from ingested `TournamentEvent`s), driven by the replication-ingest path U3 already owns. It returns empty/partial until real event-day events arrive — no schema churn later. Include a small set of ingested result event types in the projection contract.
- **B.** Stub `ResultsController` to return an empty/"not available" response for now; build the real projection when U4b/U7 land. Less code now, rework later.

[Answer]: A

### Q7 — Replication ingest authentication (US-504 ingest side)
`EventIngestController.IngestBatch` receives the hub's replicated event log. How does the cloud authenticate the hub?

- **A. (rec)** The hub authenticates as an **organizer-scoped service principal** (JWT bearer, same ASP.NET Identity issuer) authorized for that event; ingest is `[Authorize]` + event-scoped RBAC check, then idempotent `AppendIfNotExists`. Reuses the auth already built for U3; no new mechanism.
- **B.** A separate hub API-key / device-credential scheme distinct from organizer JWT. New credential type; more moving parts.
- **C.** Defer ingest auth design to U7 (which owns replication end-to-end); U3 builds the endpoint with a placeholder `[Authorize]` and a TODO. Keeps U3 smaller; couples a security decision to a later unit.

[Answer]: A

### Q8 — Any additional scope constraints?
Free-form. Anything to explicitly include or exclude from U3 functional design (e.g., account roles beyond organizer/coach/registrant/parent, rate-limiting specifics, audit-log surfacing per NFR-2.11, soft-delete vs withdrawal semantics)?

[Answer]: N/A

---

## Execution checklist (after answers approved)

- [x] Q1–Q7 answered and ambiguities resolved — all recommendations (Q1=C, Q2=A, Q3=A, Q4=A, Q5=A, Q6=A, Q7=A, Q8=N/A); no vague answers, no follow-ups
- [x] `functional-design/domain-entities.md` — two persistence planes (Q1=C), reused U1 entities as projection shapes, event vocabulary, ResultsProjection contract, ER summary, traceability
- [x] `functional-design/business-logic-model.md` — flow groups A–E for all 20 stories + ingest; universal validate→append→project write path; sequence sketch; PBT invariants; story→flow map
- [x] `functional-design/business-rules.md` — BR-AUTH/EVT/DIV/RBAC/REG/PAY/ING/RES/X families with IDs, sources, enforcement points; 4 PBT invariants; security + resiliency carry-forward notes
- [x] Testable invariants called out for the PBT suite (PBT-1 assignment determinism, PBT-2 no double-registration, PBT-3 RBAC deny-by-default, PBT-4 ingest idempotency)
- [x] Security-sensitive rules flagged (🔒) for NFR Requirements stage
- [ ] Completion message presented; await explicit approval
