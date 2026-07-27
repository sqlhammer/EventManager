# U10 HTTP Replication Adapter — Story Generation Plan

**Stage**: INCEPTION → User Stories, Part 1 (Planning)
**Assessment**: `u10-user-stories-assessment.md` — Execute = **Yes** (three High Priority criteria met independently)
**Requirements**: `inception/requirements/u10-http-replication-requirements.md` (U10-FR-1..19, U10-NFR-1..8, U10-CON-1..6)
**Existing corpus**: 66 stories / 7 epics; personas P1–P5

---

## PART 1a — Planning Questions

Please answer each question by putting the letter after the `[Answer]:` tag.

---

### Question 1 — Where do U10 stories live?

The existing corpus has 7 epics. Epic 5 "Offline Resilience & Recovery" already contains **US-504 Hub→cloud replication & outage replay** and **US-602 Post-event cloud completeness** — the two stories this unit actually delivers for real.

A) **New Epic 8 "Hub Identity & Cloud Replication"**, numbered US-801.. — matches the U9 precedent (Epic 7 / US-701..710) and keeps the increment self-contained.

B) **Fold into Epic 5**, continuing the series at US-509.. — puts replication stories where replication stories already are, at the cost of mixing MVP and post-MVP scope in one epic.

C) **Split by nature** — credential issuance/revocation into Epic 1 "Pre-Event Setup" (it is organizer setup work), replication behaviour into Epic 5. Most faithful to the user's journey, least self-contained for review.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 2 — Story breakdown approach

Trade-offs for this specific unit:

A) **User Journey-Based** — follow the organizer's arc: issue a credential → hub replicates during the event → connectivity drops → it recovers → close the event with everything mirrored. *Strength*: reads as a real day; the manual walkthrough (Q11=D) falls out of it directly. *Weakness*: system behaviour with no human in the loop (batch splitting, cursor seeding) fits awkwardly.

B) **Feature-Based** — grouped by capability: credential lifecycle / replication transport / failure handling / observability. *Strength*: clean mapping to U10-FR groups and to the code that will be written. *Weakness*: less obviously "valuable to a user", which strains INVEST.

C) **Persona-Based** — grouped by P1 Organizer vs system actors. *Strength*: simple. *Weakness*: P1 dominates almost everything here, so it degenerates into one large group.

D) **Hybrid** — journey-based for the organizer-facing arc, feature-based for the system behaviour that has no human actor. *Strength*: each kind of requirement gets the shape that suits it. *Weakness*: two organizing principles in one epic; needs a stated rule for which story a criterion belongs to.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 3 — Granularity

A) **Coarse (~5–6 stories)** — one per major capability; fewer, larger stories with long criteria lists.

B) **Moderate (~8–10 stories)** — matches U9's Epic 7 (10 stories) and the density of Epics 1–6.

C) **Fine (~13–15 stories)** — one story per U10-FR cluster; maximum testability, more overhead and more inter-story dependency.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### Question 4 — How should mechanism-only behaviour be expressed?

Several requirements have **no human actor**: failure classification (U10-FR-6), retry policy (U10-FR-7/8), the circuit breaker (U10-FR-9), batch splitting (U10-FR-13), cursor seeding (U10-FR-12). A story like "As the hub, I want to classify a 403 as permanent" is not a user story in any meaningful sense, but leaving these out means the acceptance criteria that drive the tests live nowhere.

Note the corpus already has precedent for system-actor framing — US-210 is tagged "(P3-observed, system)" and US-404 "(P4 observed, hub)".

A) **System-actor stories** — write them explicitly as hub/system stories, following the US-210 / US-404 precedent.

B) **Fold into outcome stories as acceptance criteria** — e.g. "As an organizer, I want an internet outage to cost me nothing" carries the breaker, retry, and resume criteria underneath it. Keeps every story user-valuable; hides the mechanism from the story title.

C) **Exclude from stories entirely** — capture them as business rules at Functional Design (the `BR-*` pattern used by U3/U9). Stories stay purely user-facing.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### Question 5 — U10-CON-5: should the stories decide how a credential reaches the hub?

The cloud can issue a hub credential and the hub can store one, but **nothing connects them** — the hub's MAUI UI is still a deferred seam, so there is no screen to paste a key into. Candidates: a hub admin endpoint that accepts the credential, a config-file bootstrap on first run, or a hub-initiated enrolment using a short-lived organizer token.

A) **Stay neutral** — stories state the organizer's need ("I need to get a working credential onto the hub before the event") and the observable outcome, leaving the mechanism to Functional Design.

B) **Decide now** — pick a mechanism and write it into the acceptance criteria, so Functional Design implements rather than chooses.

C) **Write the alternatives** — one story per candidate mechanism, explicitly marked as mutually exclusive options for Functional Design to select from.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 6 — What happens to US-504 and US-602?

**US-504 "Hub→cloud replication & outage replay"** and **US-602 "Post-event cloud completeness"** are marked delivered by U7 — but they were only ever satisfied by the *in-process* `StoreBackedReplicationTransport`. Over a real network they are not yet true. If new U10 stories restate the same behaviour, the corpus will contain two sets of criteria for one capability.

A) **Leave them untouched**; U10 stories reference them and cover only what is genuinely new (credential, classification, breaker, observability). Accepts that US-504 currently overstates what ships.

B) **Amend US-504/US-602** with a note that real HTTP transport arrives in U10, then add new stories only for new behaviour. Honest about history, minimal duplication.

C) **Supersede** — mark US-504/US-602 delivered-in-part and move their real acceptance criteria into the new U10 stories, so there is one authoritative place.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### Question 7 — Acceptance criteria format

A) **Given/When/Then** throughout — matches Epic 7 and the rest of `stories.md`.

B) **Bullet checklist** — terser, less precise about preconditions.

C) **Given/When/Then, plus one table** for the failure-classification matrix (U10-FR-6), since ~10 status codes × transient/permanent reads badly as prose.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

### Question 8 — Security negative cases

The cases: revoked credential, expired credential, credential for the wrong event, non-HTTPS base URL without the dev flag, credential appearing in a log line or metric label.

A) **One dedicated cross-cutting security story** — the U9 precedent (US-709).

B) **Distributed** — negative criteria attached to whichever story owns the positive behaviour.

C) **Both** — a dedicated story *and* negative criteria on each resource story. *Note*: U9 chose this at Q6 and then had to resolve the resulting duplication in a clarification round (C2), so if you pick C, expect a follow-up asking which is authoritative on conflict.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 9 — Does this unit need a new persona?

Current personas: P1 Organizer, P2 Coach, P3 Registrant, P4 Judge/Scorekeeper, P5 Check-In/Weigh-In Staff.

A) **No new persona** — the hub is a system actor and P1 Organizer performs every human action (issue, deliver, revoke, close out).

B) **Document "The Hub" as a non-human actor** in `personas.md` — no new human persona, but the system actor is named so system-actor stories have a defined subject. (Relevant if Q4=A.)

C) **Add P6 "Hub Operator / Event-day IT"** — a human distinct from the organizer who sets up and babysits the hub at the venue. Realistic for larger events; invents a role the rest of the corpus has never needed.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### Question 10 — Observability stories, given U10-CON-2

You chose F3=B: metrics go to a collector in the **cloud** stack. That collector is unreachable during exactly the outages this unit exists to survive, so at a venue with no internet the organizer's only signal is the hub's own `/health`.

A) **One organizer story** for venue-visible replication status, with U10-CON-2's limitation written in as an explicit acceptance criterion (so "the dashboard is quiet" is never mistaken for "the hub is fine").

B) **Two stories** — venue-visible status (hub `/health`) and cloud-side metrics, kept separate because they have different audiences and different availability.

C) **No observability stories** — treat it as an Operations concern and cover it in requirements only.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## PART 1b — Resolved Decisions

**Answered 2026-07-27 by the AI at the user's direction** — "proceed with your recommendations". These are
my choices, not the user's preferences; each is recorded with the reasoning so any of them can be
reversed on review.

| Question | Answer | Reasoning |
|---|---|---|
| Q1 Epic placement | **A** — new Epic 8 "Hub Identity & Cloud Replication", US-801.. | Matches the U9/Epic 7 precedent and keeps the increment reviewable as one block. B would mix post-MVP scope into an epic already marked delivered. |
| Q2 Breakdown approach | **A** — user journey-based | Chosen over the hybrid D because D needs a rule for which story owns a criterion, and Q4=B removes the need for one: with mechanism folded into outcomes, every requirement has a journey moment. The Q11=D manual walkthrough also falls straight out of a journey. |
| Q3 Granularity | **B** — moderate, ~8–10 | Matches Epic 7's density and the rest of the corpus. |
| Q4 Mechanism behaviour | **B** — fold into outcome stories as criteria | Keeps every story valuable to P1 (INVEST) and avoids "as the hub, I want to classify a 403", which is a design note wearing a story's clothes. Criteria are written as *observable* behaviour ("the hub does not retry a 403") so they stay testable without naming the mechanism. |
| Q5 U10-CON-5 stance | **A** — stay neutral | Stories should not make an architecture decision. C would put two or three mutually exclusive stories into the permanent corpus when only one will ever be built. |
| Q6 US-504/US-602 | **B** — amend with a note, add new stories for new behaviour | A leaves the corpus overstating what shipped; C rewrites approved MVP stories, which is heavier than the problem warrants. B records the truth without disturbing merged scope. |
| Q7 AC format | **C** — Given/When/Then + one table | The failure classification is ~10 status codes across two categories; as prose it would be unreadable and untestable. |
| Q8 Security negatives | **A** — one dedicated cross-cutting story | Deliberately avoids the duplication C produced in U9, which needed a clarification round (C2) to untangle. |
| Q9 Personas | **A** — no new persona | Q4=B means there are no system-actor stories, so option B's named actor has no subject to serve. P1 performs every human action in this unit. |
| Q10 Observability | **A** — one story, with U10-CON-2 as an explicit criterion | B splits an audience that is one person. Cloud-side metrics have no story-worthy consumer yet — dashboards and alerts are out of scope (§7). |

**Contradiction check (Step 9)**: no contradictions. Q2=A + Q4=B are mutually reinforcing; Q9=A follows
from Q4=B. One deliberate exception recorded: the Q8=A security story is cross-cutting rather than a
journey moment, an accepted departure from Q2=A that mirrors U9's US-709.

---

## PART 2 — Execution Checklist

*(Executed only after explicit plan approval at Step 13.)*

### Preparation
- [x] Re-read `u10-http-replication-requirements.md` (U10-FR-1..19, U10-NFR-1..8, U10-CON-1..6)
- [x] Re-read the answered verification + clarification question files for decision provenance
- [x] Re-read `stories.md` header counts, Epic 7, and the Ordering Summary to match house style
- [x] Re-read `personas.md` structure (persona blocks, Read Access Tier table, Persona → Story Map)

### Persona work
- [x] Apply the Q9 decision — add a persona/actor entry only if Q9 = B or C
- [x] Confirm no existing persona description needs amending for hub-credential responsibilities
- [x] Extend the Persona → Story Map with the new stories

### Story generation
- [x] Create the epic per the Q1 decision, with the numbering that answer implies
- [x] Generate stories using the Q2 breakdown approach at the Q3 granularity
- [x] Express mechanism-only behaviour per the Q4 decision
- [x] Handle U10-CON-5 per the Q5 decision
- [x] Apply the Q6 decision to US-504 and US-602
- [x] Write all acceptance criteria in the Q7 format
- [x] Place security negative cases per the Q8 decision
- [x] Write observability stories per the Q10 decision, including U10-CON-2's limitation if Q10 = A or B
- [x] Tag every story with its persona(s) and U10-FR references, matching existing house style

### Verification
- [x] Verify INVEST for every new story; record any deliberate violation in-document rather than glossing it
- [x] Verify every U10-FR (1–19) maps to at least one story; list any that do not and say why
- [x] Verify U10-NFR-1 (5-minute lag) and U10-NFR-2 (completeness gate) have testable criteria
- [x] Verify no acceptance criterion contradicts an approved requirement or decision D-U10-01..15
- [x] Confirm the criteria are sufficient to author the Q11=D manual walkthrough later

### Completion
- [x] Update `stories.md` header counts and Ordering Summary
- [x] Add a U10-FR → Stories traceability matrix
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting (Step 19)
- [x] Mark every checklist item above [x] as completed, in the same interaction as the work
