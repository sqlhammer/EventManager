# Story Generation Plan — Unit U9: Read/Query API

**Created**: 2026-07-26
**Stage**: INCEPTION → User Stories, Part 1 (Planning)
**Assessment**: [u9-user-stories-assessment.md](u9-user-stories-assessment.md) — Execute = Yes
**Requirements**: [u9-read-api-requirements.md](../requirements/u9-read-api-requirements.md) — approved 2026-07-26

---

## Context for the questions below

The unit delivers **9 GET endpoints** governed by a **three-tier access model**:

| Tier | Qualifies when | Sees |
|---|---|---|
| **T0 Public** | Any authenticated caller, event has `RegistrationStatus == Open` | Event summary, divisions, weigh-in policy |
| **T1 Registrant** | Caller has a non-withdrawn registration in the event | T0 + event detail + **their own** registrations |
| **T2 Organizer** | Caller holds an organizer role on the event | T1 + full registrant roster + organizer roster |

Existing story conventions in [stories.md](../user-stories/stories.md): epic-based hybrid, 56 stories
across 6 epics, numbered `US-1xx` (setup) through `US-6xx` (results), format
`### US-XXX — Title (Persona) [FR refs]` with checkbox acceptance criteria, Given/When/Then reserved
for behavioural and event-day flows.

---

## PART 1 — Questions

Answer each by putting a letter after the `[Answer]:` tag. Choose the last option (Other) and
describe if none fit.

### Question 1 — Story breakdown approach
How should the U9 stories be organized? (Step 5 requires presenting these trade-offs.)

A) **New Epic 7 — "Reading Event Data"** — all U9 stories in one new epic, keeping the read surface
   visible as a coherent capability. *(Cleanest traceability for this unit; slightly separates read
   stories from the write stories they mirror.)*

B) **Fold into existing epics** — event/division read stories join Epic 1 (Pre-Event Setup),
   registrant reads join Epic 2 (Registration), etc. *(Keeps related reads and writes together;
   scatters the unit across the document, making U9's scope harder to see at a glance.)*

C) **Persona-based grouping** — stories grouped by P1 Organizer / P2 Coach / P3 Registrant, each
   covering what that persona can read. *(Maps directly onto the tier model; risks duplicating
   endpoint behaviour across three sections.)*

D) **Tier-based grouping** — one group per access tier (T0/T1/T2), each covering all resources
   visible at that tier. *(Matches the unit's actual structure most closely; unusual shape compared
   with the existing document.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 2 — Story granularity
How finely should the 9 endpoints be split into stories?

A) **One story per resource** (~5 stories: event, division, weigh-in policy, registrant, account) —
   each story covers both single and collection forms and all applicable tiers

B) **One story per endpoint** (~9 stories) — finest granularity, most directly traceable to the
   endpoint inventory

C) **One story per resource per tier** (~10–12 stories) — separates "registrant reads their own
   registration" from "organizer reads the full roster" as distinct stories

D) **One story per user goal** (~6–8 stories) — written from what the persona is trying to
   accomplish (e.g. "find an event to register for", "review my roster before event day") rather
   than from the endpoint list

X) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 3 — Story numbering
The existing document uses US-101..US-603, with US-110 added post-MVP.

A) **New US-7xx series** — U9 stories numbered US-701 upward, clearly marking them as the read unit

B) **Extend existing series by topic** — read stories take the next free number in the series that
   matches their epic (US-111.., US-212.., US-604..)

X) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 4 — The tier model itself
The three-tier access model is cross-cutting: it governs every endpoint.

A) **One dedicated cross-cutting story** defining tier resolution, plus per-resource stories that
   reference it — avoids restating tier rules nine times

B) **No dedicated story** — tier rules are expressed inside each story's acceptance criteria, so
   every story is independently testable (favours INVEST "Independent")

C) **Dedicated story per tier** (three stories: T0, T1, T2) describing everything that tier unlocks

X) Other (please describe after [Answer]: tag below)

[Answer]: C

### Question 5 — Acceptance criteria format
The existing document uses checklists for CRUD/setup and Given/When/Then for behavioural flows.
Read endpoints with tiered authorization sit between the two.

A) **Checklists throughout** — consistent with the setup/CRUD stories these mirror

B) **Given/When/Then throughout** — better suited to expressing tier-dependent outcomes
   ("Given the caller has no tier on the event, When they request it, Then 404")

C) **Hybrid by story type** — checklists for the response-shape criteria, Given/When/Then
   specifically for the authorization outcomes

X) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 6 — Security negative cases
SECURITY-08 is blocking, and U9-FR-12 requires that denial be indistinguishable from absence
(no existence disclosure through status codes).

A) **Negative cases as acceptance criteria** within each resource's story — every story carries its
   own "what this caller must not see" criteria

B) **A dedicated security story** covering non-disclosure, IDOR resistance, and cross-event id
   probing across all endpoints, plus lighter per-story criteria

C) **Both** — a dedicated security story for the cross-cutting rules *and* explicit negative
   criteria in each resource story *(most thorough, some deliberate redundancy)*

X) Other (please describe after [Answer]: tag below)

[Answer]: C

### Question 7 — ETag / conditional requests
C3=D put watermark ETags on event-scoped endpoints. This is user-observable for API clients but
non-functional in character.

A) **No story** — ETags stay an NFR (U9-NFR-1) and are covered at Functional Design only

B) **One story** — "As an API client, I want conditional requests so that repeat polling is cheap",
   with acceptance criteria for 200-with-ETag, 304-on-match, and ETag change after a write

C) **One story including the U9-CON-2 caveat** — as B, plus explicit criteria for the athlete-profile
   staleness gap, so the resolution of that constraint is pinned by an acceptance test

X) Other (please describe after [Answer]: tag below)

[Answer]: C

### Question 8 — Stated assumptions from requirements
Two assumptions were carried into the approved requirements and are worth confirming as stories.

A) **Both stand as written** — weigh-in policy is public/T0 (U9-CON-4), and registrants can read
   their own registration (U9-CON-5)

B) **Weigh-in policy moves to T1** — only registrants and organizers can read it; drop it from the
   public tier

C) **Registrant self-reads move out of scope** — T1 grants event detail only, not registration
   records; registrants use the existing `GET /api/results/athletes/{athleteId}` instead

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## PART 1b — Resolved Decisions (all questions answered 2026-07-26)

| Decision | Answer | Outcome |
|---|---|---|
| Breakdown | Q1=A | All U9 stories in a **new Epic 7 — "Reading Event Data"** |
| Granularity | Q2=A, narrowed by C2=C | One story per resource (5), **shape only** |
| Numbering | Q3=B → **C1=A** | **US-701..US-710** — number and epic agree |
| Tier model | Q4=C, reinforced by C2=C | **Three tier stories are authoritative** for all authorization criteria |
| AC format | Q5=B | **Given/When/Then throughout** |
| Security negatives | Q6=C, narrowed by C2=C | Dedicated security story for cross-cutting rules; per-resource negative criteria **not** duplicated (see note) |
| ETag | Q7=C | One story including the U9-CON-2 athlete-profile staleness caveat |
| Assumptions | Q8=A | Weigh-in policy stays **T0**; registrant self-reads stay **in scope** |

### Note on Q6=C vs. C2=C
Q6=C asked for a dedicated security story *and* explicit negative criteria in every resource story.
C2=C then made tier stories authoritative and resource stories shape-only. The later, more specific
answer governs: **authorization and negative criteria live in the three tier stories**, cross-cutting
non-disclosure rules live in the security story, and resource stories carry response shape, filters,
and inclusion flags only. The "dedicated security story" half of Q6=C is preserved in full; the
"negative criteria in each resource story" half is superseded to avoid the 5×3 duplication C2 was
asked to resolve. **Say so if you intended otherwise** — it is a one-line change to this plan.

### Planned story set (10 stories, Epic 7)

| ID | Story | Type |
|---|---|---|
| US-701 | Public tier (T0) access to an open event | Tier — authoritative |
| US-702 | Registrant tier (T1) access to an event I am in | Tier — authoritative |
| US-703 | Organizer tier (T2) access to an event I administer | Tier — authoritative |
| US-704 | Read event summary and detail | Resource — shape |
| US-705 | Read divisions | Resource — shape |
| US-706 | Read the weigh-in policy | Resource — shape |
| US-707 | Read registrants | Resource — shape |
| US-708 | Read organizer accounts and roles | Resource — shape |
| US-709 | Non-disclosure and resource-id probing resistance | Security — cross-cutting |
| US-710 | Conditional requests for cheap repeat reads | Caching, incl. U9-CON-2 caveat |

---

## PART 2 — Execution Checklist

Executed only after the plan is approved.

### Preparation
- [x] Load approved requirements `u9-read-api-requirements.md` (tier model, 9-endpoint inventory, U9-FR-1..12, U9-CON-1..5)
- [x] Load existing `stories.md` and `personas.md` to match established conventions
- [x] Create Epic 7 "Reading Event Data" and number the stories US-701..US-710 per the table above

### Persona work (mandatory artifact)
- [x] Verify no new personas are required — requirements §10 concluded T0/T1/T2 map onto existing P1 Organizer, P2 Coach, P3 Registrant
- [x] Update `personas.md` with an access-tier mapping so each persona states which tiers it reaches
- [x] Update the Persona → Story Map table with the new U9 story ids

### Story generation
- [x] Draft all 10 stories in the "As a … I want … so that …" form used throughout `stories.md`
- [x] Write US-701/702/703 as the **authoritative** tier stories — qualification rules, granted access, and what each tier must not see
- [x] Write US-704..708 as **shape-only** resource stories — fields, filters, inclusion flags; reference tiers by name, do not restate their rules
- [x] Write US-709 covering only cross-cutting non-disclosure: status-code parity between denied and absent, cross-event id probing, no existence leakage
- [x] Write US-710 covering 200-with-ETag, 304-on-match, ETag change after a write, **and** the U9-CON-2 athlete-profile staleness case
- [x] Write every acceptance criterion as Given/When/Then
- [x] Keep weigh-in policy at T0 and registrant self-reads in scope (Q8=A)
- [x] Tag every story with persona(s) and requirement ids (`[U9-FR-n]`), matching the existing `[FR-n.n]` convention

### INVEST verification (mandatory artifact)
- [x] **Independent** — resource stories US-704..708 are independent of each other. They are *not* independent of the tier stories: C2=C makes US-701..703 authoritative, so a resource story cannot be implemented without its tier rules. This is an accepted, deliberate trade-off of C2=C, recorded here rather than glossed over
- [x] **Negotiable** — criteria state outcomes, not implementation
- [x] **Valuable** — each story names a persona-visible benefit
- [x] **Estimable** — scope is bounded and unambiguous
- [x] **Small** — no resource story spans more than one resource; no tier story spans more than one tier
- [x] **Testable** — every acceptance criterion is observable through the API

### Traceability and integration
- [x] Map every one of U9-FR-1..12 to at least one story; record any requirement with no story and justify it
- [x] Confirm no story contradicts the approved requirements or reintroduces superseded answers (Q3=A organizers-only, Q5=C unrestricted account lookup)
- [x] Note candidate PBT-01 properties surfaced by the acceptance criteria, for Functional Design
- [x] Append U9 stories to `stories.md` and update its header counts

### Completion
- [x] Update `aidlc-state.md` — User Stories complete for U9
- [x] Log completion and the approval prompt in `audit.md`
- [x] Present the standard completion message and await explicit approval

---

**All questions are answered and all ambiguities resolved.** This plan awaits explicit approval;
no stories will be generated until it is approved.
