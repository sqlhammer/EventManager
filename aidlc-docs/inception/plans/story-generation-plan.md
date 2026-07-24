# Story Generation Plan — EventManager MVP

**Status**: Answers received — plan finalized, awaiting user approval to begin generation
**Inputs**: `aidlc-docs/inception/requirements/requirements.md` (FR-1..FR-6, NFR-1..NFR-6, D-01..D-19)

## Approved Methodology (from answered questions below)

| Decision | Choice |
|---|---|
| Breakdown | **Epic-based hybrid** — epics = journey phases (Pre-Event Setup → Registration → Event Morning → Competition → Results & Wrap-Up), stories tagged by persona and FR |
| Granularity | **Medium** (~40–60 stories) — split by scenario where behavior differs |
| Acceptance criteria | **Hybrid** — Given/When/Then for behavioral/event-day flows; checklist for CRUD/setup stories |
| Prioritization | **Delivery-dependency ordering** — no per-story MoSCoW labels; ordering notes tie to unit build order |
| Personas | Five personas; combined Athlete/Parent persona named **"Registrant"** |
| Failure paths | **Separate stories** for flagship offline/failure scenarios; other failure paths folded into acceptance criteria |

---

## Execution Checklist (Part 2 — executed only after plan approval)

- [x] Step 1: Generate `aidlc-docs/inception/user-stories/personas.md` — 5 personas (Organizer, Coach, Registrant, Judge, Check-In Staff) with goals, characteristics, authority boundaries, and offline context
- [x] Step 2: Generate `aidlc-docs/inception/user-stories/stories.md` organized as epic-based hybrid (6 epics = journey phases incl. dedicated Offline Resilience epic; stories tagged persona + FR)
- [x] Step 3: Write stories following INVEST criteria; each story includes: ID, persona, narrative, acceptance criteria (hybrid Gherkin/checklist), and requirement traceability (52 stories)
- [x] Step 4: Cover all requirement areas FR-1..FR-6; dedicated offline/failure-path stories US-501..US-508; minor failure paths folded into acceptance criteria
- [x] Step 5: Persona→story map in personas.md; delivery-dependency ordering noted per epic + Ordering Summary in stories.md
- [x] Step 6: Validated: FR→story traceability matrix complete (FR-2.6 and FR-4.2 carry documented rationales); story→FR tags on every story; INVEST check noted; 52 stories (within 40–60 target)
- [x] Step 7: aidlc-state.md updated; completion message presented for approval

---

## Planning Questions

Please answer each question by filling in the letter choice after the [Answer]: tag.

## Question 1
Which story breakdown approach should organize stories.md?

A) **Feature-based** — grouped by the six requirement areas (mirrors FR-1..FR-6; cleanest traceability into units and construction)

B) **User journey-based** — grouped by lifecycle phase (pre-event setup → registration → event morning → competition → results; reads like the product's real timeline)

C) **Persona-based** — grouped by the five personas (emphasizes each user type's complete experience)

D) **Epic-based hybrid** — epics = journey phases, stories within each epic tagged by persona and FR (most structure, slightly more overhead)

E) Other (please describe after [Answer]: tag below)

[Answer]: D

## Question 2
What story granularity should be targeted?

A) **Coarse** (~20–30 stories) — one story per capability (e.g., "Coach bulk-registers athletes"); faster to review, details live in acceptance criteria

B) **Medium** (~40–60 stories) — capabilities split by scenario where behavior differs (e.g., bulk registration happy path vs. duplicate handling as separate stories)

C) **Fine** (~70+ stories) — every distinct scenario and edge case its own story

D) Other (please describe after [Answer]: tag below)

[Answer]: B

## Question 3
What acceptance criteria format should be used?

A) **Given/When/Then (Gherkin)** — scenario-oriented; maps directly to test cases (incl. the PBT/example-based tests the Testing extension requires)

B) **Checklist bullets** — simple verifiable statements per story; lighter to read

C) **Hybrid** — Given/When/Then for behavioral/event-day flows; checklist for CRUD/setup stories

D) Other (please describe after [Answer]: tag below)

[Answer]: C

## Question 4
How should stories be prioritized?

A) **MoSCoW** (Must/Should/Could/Won't-this-release) per story

B) **Ordered by delivery dependency** — priority emerges from unit build order rather than per-story labels

C) **Both** — MoSCoW labels plus dependency ordering notes

D) Other (please describe after [Answer]: tag below)

[Answer]: B

## Question 5
The requirements identify five personas (Organizer, Coach, Athlete/Parent, Judge, Check-In Staff). Should any be split or added? (e.g., split Athlete vs. Parent-of-minor into separate personas; add a "Tournament Director assistant" secondary admin role)

A) Keep the five as-is; treat Parent-of-minor as a variant noted inside the Athlete persona

B) Split Athlete and Parent-of-minor into six personas (registration flows differ meaningfully)

C) Other (please describe after [Answer]: tag below)

[Answer]: C. Keep the five as-is but name the Athlete/Parent persona, "Registrant". It will still be a combined persona for Athlete and Parent but this will be the name.

## Question 6
Should stories include explicit **negative/failure-path stories** (e.g., "As a judge, when the hub is unreachable, my scores queue locally and sync later"), or fold failure paths into acceptance criteria of the primary stories?

A) Fold failure paths into acceptance criteria of primary stories (fewer stories, richer criteria)

B) Separate stories for the flagship offline/failure scenarios (makes the differentiator visible and independently testable), acceptance-criteria folding for everything else

C) Other (please describe after [Answer]: tag below)

[Answer]: B
