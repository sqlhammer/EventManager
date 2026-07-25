# Functional Design Plan — U1 Shared Core

**Stage**: CONSTRUCTION - U1 Shared Core - Functional Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Unit**: U1 = `EventManager.Domain` (entities + bracket/seeding/scoring/weigh-in/RBAC engines) + `EventManager.Sync` (event log, replay, projections, Snowflake, replication protocol).
**Altitude**: Detailed, technology-agnostic business logic. No infrastructure/framework concerns.
**PBT note**: This unit is the mandated property-based-testing surface (NFR-4.3) — answers here define the invariants tests will assert.

---

## Part 1 — Functional Design Questions

Business-rule questions (you are the domain authority) and a few core-mechanic questions. Answer each `[Answer]:` tag; "Other" (last) if none fit. Recommendations noted.

### Question 1 — Point-sparring win determination (FR-6.2)
How is a point-sparring match won?

A) **Higher total points at match end wins**, with *optional* per-event early-finish rules: a target score (first to N ends it) and a point-gap "mercy" threshold — both configurable, off by default (recommended: flexible, extensible)

B) Higher total points at end only — no target/mercy rules in MVP (simplest)

C) Target-score race only (first to N points)

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 2 — Point-sparring penalties (FR-6.2)
How do penalties affect the score?

A) Each penalty **awards one point to the opponent**; reaching a penalty cap (e.g., 3) results in disqualification (recommended)

B) Each penalty **deducts one point** from the offender

C) Penalties are tracked and only a cap triggers DQ (no per-point effect)

D) **Configurable per event** (choose award/deduct + cap)

E) Other (please describe after [Answer]: tag below)

[Answer]: D

### Question 3 — Forms/kata aggregation & tie-break (FR-6.2, US-403)
How are per-judge forms scores aggregated and ties broken?

A) **Average of judge scores; drop highest and lowest when ≥5 judges**; ties broken by highest remaining single score, then a judged runoff (recommended)

B) Simple average, no high/low drop

C) Sum of all judge scores

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 4 — Round-robin standings tie-break (FR-3.2, US-312)
Order of tie-break criteria for round-robin standings?

A) **Wins → head-to-head (if exactly two tied) → point differential → manual/coin** (recommended)

B) Wins → point differential → head-to-head → manual

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 5 — Seeding, academy separation & byes (FR-3.3, FR-3.5, US-311/313)
How aggressively are same-academy athletes separated, and how are byes placed?

A) **Distribute same-academy athletes across halves, then quarters, as far as bracket size allows; byes assigned to top seeds first** (standard) (recommended)

B) Separate only enough to avoid first-round same-academy matches; byes placed randomly

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 6 — Weigh-in tolerance semantics (FR-5.3, D-11)
When policy = tolerance X%, X% of what, and in which direction?

A) **X% of the division's upper weight limit, over-limit only** (under limit always passes); e.g., 2% over a 70.0 cap passes ≤ 71.4 (recommended)

B) X% applied to both over- and under-bounds of the class

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 7 — Canonical replay/apply order (projection determinism)
Events originate on multiple devices (Snowflake `EventId` time-sortable + per-device contiguous `SequenceNumber`). What total order do projections fold in, so state is deterministic?

A) **Order by `EventId` (time-sortable) globally**; `SequenceNumber` used only for gap detection/replication, not apply order (recommended: simple, deterministic, ≈ time order)

B) Apply per-device streams in `SequenceNumber` order, merged across devices by an `EventId` tiebreak (stricter per-device causality, more complex)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 8 — Snowflake bit layout & clock-regression policy (D-26)
Confirm the generator's layout and behavior when the clock moves backwards.

A) **41-bit ms timestamp (custom epoch 2026-01-01) + 10-bit worker (1024) + 12-bit sequence (4096/ms/worker)**; on clock regression, briefly wait for the clock to catch up; refuse/alarm if regression exceeds a small threshold (recommended, standard Snowflake)

B) A different layout or epoch (specify)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 9 — Event payload versioning (NFR-6.3 upgrade path)
How do event payloads evolve while remaining replayable forever?

A) **Each event type carries a schema-version integer; on replay, old payloads are upcast to the current shape**; stored events are never mutated (recommended)

B) Additive-only fields, no explicit version (rely on tolerant readers)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `construction/u1-shared-core/functional-design/domain-entities.md` — entities/value objects, identities (Snowflake), relationships, invariants
- [x] Generate `construction/u1-shared-core/functional-design/business-logic-model.md` — engines & event-sourcing flows (bracket gen/advance, seeding, scoring, weigh-in eval, replay/fold, projection rebuild, Snowflake gen, replication protocol)
- [x] Generate `construction/u1-shared-core/functional-design/business-rules.md` — decision rules, validation, constraints, and the PBT invariants each rule implies
- [x] (Frontend-components.md — **N/A**: U1 is a library, no UI)
- [x] Validate design against U1 stories/FRs and extension obligations (PBT invariants enumerated; Security/Resiliency touchpoints noted)
- [x] Update aidlc-state.md; log approval in audit.md
