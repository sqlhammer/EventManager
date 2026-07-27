# Story Planning Clarification Questions — Unit U9

**Stage**: INCEPTION → User Stories, Part 1 (Step 10 — mandatory follow-up)
**Created**: 2026-07-26
**Source answers**: `u9-story-generation-plan.md` (Q1=A, Q2=A, Q3=B, Q4=C, Q5=B, Q6=C, Q7=C, Q8=A)

Six of your eight answers are unambiguous and need no follow-up: Q5=B (Given/When/Then throughout),
Q7=C (ETag story including the U9-CON-2 caveat), Q8=A (both assumptions stand). Two combinations
need a decision rule before generation.

---

## Contradiction 1: Epic 7 (Q1=A) vs. topic-based numbering (Q3=B)

- **Q1=A** puts **all** U9 stories in a single **new Epic 7 — "Reading Event Data"**
- **Q3=B** numbers stories by *"the next free number in the series that matches their epic
  (US-111.., US-212.., US-604..)"*

Q3=B's rule presupposes that stories live in the epic their number matches — which is what Q1=**B**
(fold into existing epics) would have done. Under Q1=A every U9 story lives in Epic 7, so "the
series that matches their epic" has no referent. Concretely: a registrant-roster read story sits in
Epic 7 but is topically Epic 2 — should it be `US-212` or `US-704`?

### Clarification Question 1
How should Epic 7 stories be numbered?

A) **US-7xx series** — `US-701`+ matching the new Epic 7. *(Number and epic agree; simplest to read
   and extend. Effectively Q3=A.)*

B) **Topic-affinity numbering inside Epic 7** — each story takes the next free number in the series
   of the epic it *mirrors* (event reads `US-111`+, registrant reads `US-212`+, results-adjacent
   reads `US-604`+), while all still living in Epic 7. *(Preserves your Q3=B intent — the number
   signals which write-side capability the read mirrors — at the cost of an epic whose story
   numbers are non-contiguous.)*

C) **Switch to Q1=B and fold into existing epics** — abandon Epic 7; each read story lives in the
   epic it mirrors, and Q3=B's numbering then applies literally. *(Fully consistent, but gives up
   the at-a-glance view of U9's scope.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Ambiguity 2: Overlapping story sets (Q2=A + Q4=C + Q6=C)

Three of your answers each independently specify tier behaviour:

- **Q2=A** — one story per resource (~5), *"each story covers both single and collection forms **and
  all applicable tiers**"*
- **Q4=C** — one story per tier (3), *"describing **everything that tier unlocks**"*
- **Q6=C** — plus a **dedicated security story** for non-disclosure and IDOR rules

As written, the same behaviour gets specified in up to three places. "Organizer reads the full
registrant roster; a non-organizer gets 404" would appear in the registrant resource story, in the
T2 tier story, and in the security story. That is roughly a 5×3 grid of duplicated criteria, and it
weakens INVEST *Independent* and *Small* — plus, when two stories disagree after a later edit, there
is no rule for which one wins.

You already accepted *"some deliberate redundancy"* at Q6=C, so the goal here is not to eliminate
overlap — it is to give each story set a distinct job.

### Clarification Question 2
How should responsibility be divided?

A) **Tier stories own authorization; resource stories own shape.** The three tier stories define
   *who qualifies for the tier and what they must not see* (the authorization contract). The five
   resource stories define *the response fields, filters, and inclusion flags*, referencing tiers by
   name without restating their rules. The security story covers only cross-cutting rules that
   belong to no single tier or resource (cross-event id probing, status-code non-disclosure).
   *(Cleanest separation; each behaviour has exactly one authoritative home. ~9 stories.)*

B) **Resource stories are authoritative; tier stories are summaries.** Each resource story fully
   specifies all tiers for that resource. The three tier stories are readable roll-ups with no
   independent criteria. *(Resource stories stay self-contained and independently testable; tier
   stories add navigation rather than specification. ~9 stories, 3 of them non-normative.)*

C) **Tier stories are authoritative; resource stories are summaries.** The inverse of B — the three
   tier stories carry every criterion, and the resource stories describe shape only. *(Best match to
   how the unit is actually built, since tier resolution is the shared mechanism; makes any single
   resource harder to review in isolation.)*

D) **Accept full duplication** — every story states its own complete criteria, with the security
   story as the tiebreaker of record when two disagree. *(Maximum independence and testability;
   largest document and highest maintenance cost.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

**Answer both and tell me you're done.** I will then present the plan for approval, and generate no
stories until you approve it.
