# U1 Shared Core — Business Rules & PBT Invariants

**Stage**: CONSTRUCTION - U1 Shared Core - Functional Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Purpose**: Enumerate decision rules, validation, and constraints — and the **property-based-testing invariants** each implies (NFR-4.3, PBT extension full enforcement). Every rule below is a candidate FsCheck property with a seedable generator.

---

## 1. Event log & replay (Sync)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-1.1 | Events are append-only; corrections are new events | No API mutates a stored event |
| BR-1.2 | `EventId` globally unique; dedupe on append | `Append(Append(e)) == Append(e)` |
| BR-1.3 | Replay idempotence | `apply(apply(log)) == apply(log)` |
| BR-1.4 | Canonical fold order = ascending `EventId` (Q7) | Projection state independent of input arrival order; equals fold over EventId-sorted log (oracle) |
| BR-1.5 | Per-device `SequenceNumber` contiguous & gap-free | `HighWaterMark` advances only over contiguous sequences; gaps detected |
| BR-1.6 | Serialization round-trips | `deserialize(serialize(e)) == e` for all event types/versions |
| BR-1.7 | Upcasting preserves meaning; monotonic version (Q9) | `upcast` is idempotent at CURRENT; old→new never loses required fields |

## 2. Snowflake (Q8)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-2.1 | IDs monotonic per worker | For a single generator, `id_{n+1} > id_n` |
| BR-2.2 | Uniqueness within worker/ms | ≤4096 ids per ms per worker; no collision under burst |
| BR-2.3 | Clock regression handled | Regression ≤ MAX_WAIT waits; beyond raises, never emits a smaller id |
| BR-2.4 | Decode(Encode) round-trip | `(ts,worker,seq)` recovered exactly from id |

## 3. Brackets & seeding
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-3.1 | Participant preservation | Every registered athlete appears exactly once in the bracket |
| BR-3.2 | Byes only for non-power-of-two, to top seeds first (Q5) | `byes == nextPow2(n) - n`; assigned to highest seeds |
| BR-3.3 | Single-elim yields exactly one champion | Completed bracket ⇒ one winner |
| BR-3.4 | Valid advancement | Winner of match M fills the correct next slot; no athlete in two live slots |
| BR-3.5 | Round-robin completeness | Each unordered pair scheduled exactly once |
| BR-3.6 | Academy separation when feasible (Q5) | Same-academy athletes not paired before the round implied by group size, when size permits |
| BR-3.7 | Regeneration allowed pre-start only | Post-`Started` structural change requires explicit organizer action (enforced in U4b) |

## 4. Scoring
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-4.1 | Point-sparring winner deterministic (Q1) | Given entries+config, winner is a pure function; higher total wins absent early-finish |
| BR-4.2 | Penalty policy configurable (Q2=D) | AwardOpponent/DeductOffender applied per config; `cap` reached ⇒ DQ |
| BR-4.3 | DeductOffender floor | Deducted score clamped at 0 (never negative) |
| BR-4.4 | Early finish (Q1) | If `targetScore` reached ⇒ that competitor wins; if `mercyGap` reached ⇒ leader wins |
| BR-4.5 | DQ always loses | A disqualified competitor cannot be the winner |
| BR-4.6 | Forms drop rule (Q3) | Drop one high + one low iff judges ≥ 5; else average all |
| BR-4.7 | Forms aggregation order-independent | Aggregate invariant to score-entry ordering |
| BR-4.8 | Forms tie-break (Q3) | Highest remaining single score; then Runoff |

## 5. Weigh-in policy (Q6)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-5.1 | Under lower bound always passes | weight < lowerBound ⇒ Pass |
| BR-5.2 | Within class passes | weight ≤ upperBound ⇒ Pass |
| BR-5.3 | Strict ⇒ DQ pending confirm | over + Strict ⇒ Dq |
| BR-5.4 | Tolerance boundary (Q6) | over + Tolerance ⇒ Pass iff weight ≤ upperBound*(1+X/100), inclusive; else Dq |
| BR-5.5 | AutoMove target validity | Proposed division fits weight and is not `Started` |
| BR-5.6 | Evaluator is pure | outcome = f(weight, division, policy) only |
| BR-5.7 | Recommendation is non-binding (D-25) | Recommendation never changes evaluator outcome |

## 6. RBAC (FR-2.8)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-6.1 | Deny by default | Unknown/absent assignment ⇒ not permitted |
| BR-6.2 | FullAdmin-only set enforced | Co-Organizer denied {DeleteEvent, RemoveOrganizer, DemoteOrganizer, TransferFullAdmin} |
| BR-6.3 | Co-Organizer parity elsewhere | All non-FullAdmin-only actions permitted for both roles |
| BR-6.4 | ≥1 FullAdmin invariant | An event always retains at least one FullAdmin (demote/remove guarded) |

## 7. Registration/division data rules (shared model)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-7.1 | No double-registration per division | Registration idempotent on (athlete, division) |
| BR-7.2 | Division assignment matches criteria | Athlete assigned only to divisions whose criteria fit profile |
| BR-7.3 | Registration snapshots profile | Later profile edits don't retroactively change a registration |

---

## Extension touchpoints (design altitude)
- **PBT**: every BR above maps to ≥1 property; generators needed for events, brackets, divisions, weights, score entries, role assignments (NFR-4.2). Seeds logged in CI (PBT-08).
- **Security**: BR-6.x (RBAC deny-by-default) and BR-1.1 (append-only/auditable) are the security-relevant rules in U1; validation of inputs occurs at the boundary (U2/U3/U4a) before events are minted.
- **Resiliency**: BR-1.3/1.5 and the replication protocol underpin zero-data-loss; U7 owns the cross-cutting integration and its tests.

## Validation summary
- All U1 stories/FRs covered: scoring (US-402/403), brackets/seeding (US-311/312/313), weigh-in eval (US-307/308), RBAC (US-109), event-sourcing mechanics (cross-cutting FR-4.2), Snowflake (D-26).
- Frontend components: **N/A** (U1 is a library).
