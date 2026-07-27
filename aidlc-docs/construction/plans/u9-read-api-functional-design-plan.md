# Functional Design Plan — Unit U9: Read/Query API

**Created**: 2026-07-26
**Stage**: CONSTRUCTION → Functional Design (Part 1: plan + questions)
**Branch**: `unit/u9-read-api`
**Inputs**: approved requirements (U9-FR-1..12, U9-NFR-1..9, U9-CON-1..5) · Epic 7 US-701..US-710 · personas tier map

**Resolved before this stage**: **U9-CON-1 — API-local read authorizer.** The shared
`OrganizerAction` enum is not extended; `shared/EventManager.Domain` and `admin/EventManager.Hub`
are untouched.

---

## PART 1 — Questions

Answer each with a letter after the `[Answer]:` tag. Pick the last option (Other) and describe if
none fit.

### Question 1 — U9-CON-2: how should the ETag avoid serving stale athlete data?

US-710 pins the requirement ("must not receive a 304 carrying the pre-update weight") but not the
mechanism. Recap of the problem: the watermark is `MAX(EventId) WHERE EventScopeId = {eventId}`, but
athlete profile events are appended with the **athlete id** as scope, not the event id
([RegistrationService.cs:43-44](../../../backend/EventManager.Api/Services/RegistrationService.cs#L43-L44)).
Registrant detail returns profile fields, so a profile edit does not move the event watermark.

A) **Exclude registrant detail from ETag coverage** — endpoint 7 returns no ETag and never 304s.
   Every other event-scoped endpoint keeps the cheap single-watermark ETag. *(Simplest and provably
   correct. Cost: the one endpoint a client might poll per-athlete loses caching.)*

B) **Composite watermark** — for registrant detail, the ETag is `MAX` over the event scope **and**
   the referenced athlete's scope (two indexed lookups, since the response names exactly one
   athlete). *(Keeps caching everywhere and is exact. Cost: a second query on that endpoint, and the
   composition rule must be documented so it is not broken later.)*

C) **Composite watermark on both registrant endpoints** — as B, and the registrant *list* ETag
   spans the event scope plus every athlete scope it references. *(Complete coverage. Cost: the list
   variant is an aggregate over N athlete scopes, which is the expensive case, and re-introduces a
   query proportional to roster size — in tension with U9-NFR-9.)*

D) **Stop returning profile fields on registrant detail** — drop date of birth, weight, rank, and
   gender so the endpoint reads only event-scoped data and the simple watermark is correct.
   *(Eliminates the problem at the source, but overturns Q6=B and removes data organizers need for
   weigh-in checks. Listed for completeness; not recommended.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 2 — Registrations belonging to deleted accounts

**Finding**: account self-deletion (US-110) anonymizes only the **identity record** — email,
credentials, `DeletedAt` ([AccountDeletionService.cs:66-82](../../../backend/EventManager.Api/Services/AccountDeletionService.cs#L66-L82)).
It does **not** touch `AthleteProfileRow` or `RegistrationRow`. So after an account is deleted, its
athletes' registrations survive with **real names, dates of birth, and weights intact**, and U9
would expose them through the roster endpoints.

This is a live decision because an athlete registered by a now-deleted parent or coach account is
still entered in the tournament — the organizer needs to see them on the mat.

A) **Show them normally** — the roster is about who is competing, not about account lifecycle. A
   deleted managing account changes nothing about the athlete's entry. *(Operationally correct;
   means "deleting your account" does not remove your athletes' data from organizer views, which
   should be stated plainly in the testing guide.)*

B) **Show them, flagged** — included in the roster but marked as having no active managing account,
   so organizers know nobody will answer a contact attempt. *(Same data, better operational signal.)*

C) **Exclude by default, include via flag** — treat them like withdrawn registrations under the Q8=A
   pattern. *(Consistent with the existing inclusion-flag rule, but an organizer reading the default
   roster would be missing athletes who are physically present — a real event-day hazard.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 3 — U9-FR-10's "soft-deleted accounts" clause cannot fire

**Finding**: U9-FR-10 says withdrawn registrations, completed divisions, **and soft-deleted
accounts** are excluded by default with an opt-in flag. The first two are implementable. The third
has no effect: deletion appends `OrganizerRemoved` for every role the account held
([AccountDeletionService.cs:48-53](../../../backend/EventManager.Api/Services/AccountDeletionService.cs#L48-L53)),
and the projection deletes the `OrganizerRow`. Since US-708 reads exactly that table, a deleted
account can never appear in an organizer roster — there is nothing for a flag to include.

A) **Drop the clause** — amend U9-FR-10 to cover withdrawn registrations and completed divisions
   only, and record why. *(Honest; the requirement stops describing behaviour that cannot occur.)*

B) **Keep a defensive filter** — implement an explicit "exclude accounts with `DeletedAt` set"
   check anyway, as defence in depth if a future change stops detaching roles on deletion.
   *(Costs one predicate; guards against a plausible regression.)*

C) **Keep the clause and add the inclusion flag** — implement `?includeDeleted=true` even though it
   returns nothing today. *(Not recommended — ships a parameter with no observable effect.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 4 — What makes an event publicly discoverable (T0)?

US-701 grants T0 when `RegistrationStatus == Open`. But `EventRow` also carries
`RegistrationStart` and `RegistrationEnd` dates, and status is changed by explicit organizer action
(`POST /registration/open` and `/close`). Nothing forces the two to agree — an organizer who never
calls `/close` leaves an event `Open` with a registration window that ended months ago, publicly
discoverable indefinitely.

A) **Status only** — exactly as US-701 is written. Status is the organizer's explicit intent, and an
   event left open is the organizer's own choice. *(Simplest; matches the approved story verbatim.)*

B) **Status AND current date within the registration window** — T0 requires `Open` *and*
   `RegistrationStart <= today <= RegistrationEnd`. *(Prevents stale events lingering in public
   listings; means an event can be `Open` yet undiscoverable, which may confuse organizers testing
   their own event.)*

C) **Status only for discovery, but surface the window** — as A, with the response carrying the
   registration window so clients can present an event as expired without the API hiding it.
   *(Keeps the API honest and pushes presentation to the client.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## PART 2 — Execution Checklist

Executed after the questions are answered and the design is generated.

### Analysis
- [x] Confirm unit scope against U9-FR-1..12 and Epic 7 US-701..US-710
- [x] Map each of the 9 endpoints to the read-model tables it reads
- [x] Record the invariant that `RegistrationRow.ManagedByAccountId` always equals
      `AthleteProfileRow.OwnerAccountId`, enforced by BR-REG-8 at both registration paths
      ([RegisterAsync](../../../backend/EventManager.Api/Services/RegistrationService.cs#L58),
      [RegisterBatchAsync](../../../backend/EventManager.Api/Services/RegistrationService.cs#L87)),
      and note that T1 resolution must be revisited if profile-ownership transfer is ever added

### Domain entities (`domain-entities.md`)
- [x] Document the read-model rows this unit consumes, as read-only projections
- [x] Define the tier value object (T0/T1/T2) and its cumulative ordering
- [x] Define the response shapes: event summary, event detail, registrant list item, registrant detail, division, weigh-in policy, organizer account
- [x] State explicitly that this unit introduces no persisted entity, event type, or migration

### Business logic model (`business-logic-model.md`)
- [x] Design the **API-local read authorizer** per the U9-CON-1 decision — inputs, outputs, and how it composes the three tier checks
- [x] Design tier resolution for a single event and for the collection endpoint without N+1 queries (U9-NFR-9)
- [x] Design the query services per resource, and the shape-selection rule driven by resolved tier
- [x] Design ETag derivation per the Question 1 answer, including the opaque-token rule below
- [x] Specify that the ETag is an opaque hash, never the raw watermark value — a Snowflake would leak event-log volume and last-activity timing
- [x] Define the error model: 401 unauthenticated, 404 for both "no tier" and "does not exist", 400 for malformed input

### Business rules (`business-rules.md`)
- [x] Write BR-READ-* rules for tier qualification, cumulative grants, and per-tier response shape
- [x] Write the non-disclosure rules from US-709 as testable statements
- [x] Write the inclusion-flag rules per Q8=A and the Question 3 answer
- [x] Write the deleted-account rule per the Question 2 answer
- [x] Write the T0 discoverability rule per the Question 4 answer

### Testable properties (PBT-01 — blocking)
- [x] Identify properties per component and tag each with its category
- [x] Candidate *invariant*: for all caller/event pairs where the caller holds no tier, every endpoint returns 404 and no response body field
- [x] Candidate *invariant*: a T0 response never contains a detail-tier or roster field, for all generated events
- [x] Candidate *oracle*: query service results equal a naive in-memory filter over the same rows
- [x] Candidate *idempotence*: repeated identical GETs with an unmoved watermark return identical bodies
- [x] Candidate *invariant*: tier resolution is monotonic — adding a registration or organizer role never reduces a caller's tier
- [x] Mark any component with no identifiable property as such, with rationale

### Verification
- [x] Every U9-FR maps to at least one business rule or logic element
- [x] No design element contradicts the approved requirements or reintroduces superseded answers
- [x] Confirm nothing in the design touches `shared/` or `admin/` (U9-CON-1 decision)
- [x] Confirm the design records U9-CON-3 — watermark validity depends on projection staying inline
- [x] CS-1 noted for code generation: no ternary operators

### Completion
- [x] Write the three artifacts under `aidlc-docs/construction/u9-read-api/functional-design/`
- [x] Update `aidlc-state.md` and log to `audit.md`
- [x] Present the standard 2-option completion message and await approval

---

**All questions answered (Q1=A, Q2=A, Q3=A, Q4=C); no ambiguities found. Artifacts generated.**
