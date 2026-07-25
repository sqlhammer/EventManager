# U1 Shared Core — Business Logic Model

**Stage**: CONSTRUCTION - U1 Shared Core - Functional Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Altitude**: Algorithms & flows, technology-agnostic. Pure functions where possible (no I/O) — the PBT surface.

---

## 1. Event-sourcing mechanics (`EventManager.Sync`)

### 1.1 Snowflake generation (Q8=A)
```
NextId():
  now = currentMillis() - EPOCH            # EPOCH = 2026-01-01
  if now < lastTimestamp:                  # clock regression
      if (lastTimestamp - now) <= MAX_WAIT: spin until now >= lastTimestamp
      else: raise ClockRegressionError (alarm)
  if now == lastTimestamp:
      sequence = (sequence + 1) & 0xFFF     # 12 bits
      if sequence == 0: spin to next millis
  else:
      sequence = 0
  lastTimestamp = now
  return (now << 22) | (workerId << 12) | sequence
```
Layout: 41-bit ms | 10-bit worker | 12-bit sequence. Monotonic per worker; ~time-ordered globally.

### 1.2 Idempotent append
```
AppendIfNotExists(evt):
  if store.exists(evt.EventId): return false   # replay no-op
  store.append(evt); return true
```
Property: `Append(Append(e)) == Append(e)` (NFR-4.3 replay idempotence).

### 1.3 Canonical replay / projection fold (Q7=A)
- **Canonical order = ascending `EventId`** (time-sortable). `SequenceNumber` is used for gap detection/replication only, not fold order.
```
Rebuild(projection):
  state = projection.empty()
  for evt in store.readAll() ordered by EventId ascending:
      state = projection.apply(state, upcast(evt))
  return state
Dispatch(evt): state = projection.apply(state, upcast(evt))   # incremental
```
Property: `Rebuild(log) == fold(apply, empty, sort_by_eventid(log))`, and re-applying any already-folded event is a no-op (projection oracle test).

### 1.4 Payload upcasting (Q9=A)
```
upcast(evt):
  p = evt.Payload
  while p.schemaVersion < CURRENT[evt.EventType]:
      p = UPCASTERS[evt.EventType][p.schemaVersion](p)
  return evt with Payload=p
```
Stored events never mutated; upcast is applied on read only.

### 1.5 Replication protocol (hub↔cloud; used by U7)
```
NextBatch(peerHighWaterMarks):
  for each deviceId: send events with SequenceNumber in (peerHWM[deviceId] .. localHWM] ordered by SequenceNumber
DetectGaps(deviceId): return contiguous-sequence gaps below localHWM
```
Property: after any outage, resume from `LastAckedSequence` with no gaps, no duplicates (US-504).

---

## 2. Bracket engine (`EventManager.Domain`)

### 2.1 Single-elimination generation (FR-3.2/3.5)
```
GenerateSingleElim(division, seeds):
  n = seeds.count
  size = nextPow2(n)
  byes = size - n                      # byes to top seeds first (Q5=A)
  place seeds into standard seeding order (1 vs size, 2 vs size-1, ...)
  top `byes` seeds receive a first-round Bye (auto-advance)
  emit Match[] for each round; participant appears exactly once
```
Properties: participant preservation; each athlete appears exactly once; byes only when `n` not a power of two; exactly one champion at completion.

### 2.2 Round-robin generation (FR-3.2)
```
GenerateRoundRobin(division, registrations):
  produce every unordered pair exactly once (circle method)
```
Property: each athlete plays every other exactly once.

### 2.3 Advancement (FR-6.3)
```
Advance(bracket, outcome):
  single-elim: winner fills next round slot; if opponent was Bye, already advanced
  round-robin: record result; recompute standings
```
Property: applying an outcome preserves bracket validity; re-applying the same outcome is a no-op (idempotent with the event log).

## 3. Seeding engine (FR-3.3, Q5=A)
```
Seed(registrations, options):
  base = deterministic-random(order, options.seed)     # seedable for tests
  group by academy
  distribute same-academy athletes across halves, then quarters, as far as size allows
  return Seed[] (manual override applied later by hub)
```
Property (academy separation): when bracket size permits, no two same-academy athletes meet before the round implied by group size; when not possible, degrade gracefully (documented).

## 4. Scoring engine (FR-6.2)

### 4.1 Point sparring (Q1=A, Q2=D)
```
ScorePointSparring(entries, config):
  tally points per competitor; apply penalties per config.penaltyPolicy:
     AwardOpponent: each penalty +1 to opponent
     DeductOffender: each penalty -1 to offender
     if penaltyCount[c] >= cap: c is Disqualified (capAction)
  early finish (if configured): targetScore reached -> that competitor wins;
                                mercyGap reached  -> leader wins
  else: higher total wins; tie -> Decision (judge resolves)
```
Properties: winner is deterministic given entries+config; DQ always loses; score never negative below 0 floor if DeductOffender (documented clamp rule in business-rules).

### 4.2 Forms / kata (Q3=A)
```
ScoreForms(scoreEntries, config):
  per competitor: if judgeCount >= config.dropHighLowThreshold: drop one highest + one lowest
                  aggregate = average(remaining)
  rank by aggregate desc
  tie-break: highest remaining single score; still tied -> Runoff (re-score)
```
Property: aggregate invariant to input order; drop applied only when judges ≥ threshold.

## 5. Weigh-in policy evaluator (FR-5.3, Q6=A)
```
Evaluate(weighIn, division, policy):
  if weight <= division.weightClass.upperBound: Pass
  else switch policy.mode:
     Strict:    Dq (pending organizer confirm)
     AutoMove:  find division whose criteria fit weight & not Started -> propose Moved(target)
     Tolerance: if weight <= upperBound * (1 + tolerancePercent/100): TolerancePass
                else: fall back to Strict -> Dq
  (under lowerBound never fails — Q6=A)
```
Property: outcome is a pure function of (weight, division, policy); tolerance boundary is inclusive at the computed cap.

## 6. RBAC policy (FR-2.8)
```
IsPermitted(assignment, action):
  FullAdminOnly = { DeleteEvent, RemoveOrganizer, DemoteOrganizer, TransferFullAdmin }
  if action in FullAdminOnly: return assignment.role == FullAdmin
  return assignment.role in { FullAdmin, CoOrganizer }   # all other organizer actions
```
Property: deny-by-default; Co-Organizer can never perform a FullAdminOnly action; enforced identically in cloud (U3) and hub (U4a).

---

## Cross-references
- Entities/VOs: `domain-entities.md`
- Rules & invariants (PBT): `business-rules.md`
