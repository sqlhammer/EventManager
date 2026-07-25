# U1 Shared Core — Domain Entities

**Stage**: CONSTRUCTION - U1 Shared Core - Functional Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Altitude**: Technology-agnostic domain model — entities, value objects, identities, relationships, invariants. No persistence/framework detail.
**Identity**: all cross-app identities are 64-bit Snowflake IDs (D-26). Entities are immutable; state changes are events (`TournamentEvent`), never in-place mutation.

---

## A. Sync model (`EventManager.Sync`)

### TournamentEvent (the atom of state)
| Field | Type | Notes |
|---|---|---|
| `EventId` | Snowflake | PK, idempotence key, canonical sort key (Q7) |
| `DeviceId` | Snowflake | origin device/node |
| `SequenceNumber` | long | per-device contiguous, gap-free (Q9-replication) |
| `EventType` | string/enum | e.g. `MatchScored`, `CheckedIn`, `WeighInRecorded` |
| `SchemaVersion` | int | payload schema version; upcast on replay (Q9=A) |
| `Payload` | typed payload | one contract per `EventType` (in U2 Contracts) |
| `OccurredAt` | timestamp (UTC) | origin wall-clock; informational |
| `EventScopeId` | Snowflake | the tournament/event the log belongs to |

**Invariants**: `EventId` globally unique; `(DeviceId, SequenceNumber)` unique and contiguous per device; events are append-only and never edited (corrections are new events).

### Supporting sync value objects
- **WorkerIdAssignment** `{ DeviceId, WorkerId (0–1023), EventScopeId }` — unique WorkerId within an event scope (Q10/Q8).
- **ReplicationCursor** `{ DeviceId, LastAckedSequence }` — per-device high-water mark for gap-free replication.
- **SeqRange** `{ DeviceId, FromSeq, ToSeq }` — a detected gap.
- **SnowflakeLayout** (constant) `{ Epoch=2026-01-01T00:00:00Z, TimestampBits=41, WorkerBits=10, SequenceBits=12 }` (Q8=A).

---

## B. Domain model (`EventManager.Domain`)

### Core entities
| Entity | Key fields | Relationships / notes |
|---|---|---|
| **EventDefinition** | `EventId`, name, venue, date, registrationWindow, entryFees, `WeighInPolicy`, `ScoringConfig` | root aggregate for a tournament |
| **OrganizerRoleAssignment** | `Id`, `EventId`, `AccountId`, `Role {FullAdmin, CoOrganizer}` | RBAC (FR-1.6); ≥1 FullAdmin per event invariant |
| **Division** | `DivisionId`, `EventId`, `DivisionCriteria`, `BracketFormat {SingleElim, RoundRobin}`, `status {NotStarted, Started, Complete}` | groups registrations |
| **AthleteProfile** | `AthleteId`, name, DOB, rank, weight, academy | owned by a Registrant account |
| **Registration** | `RegistrationId`, `EventId`, `AthleteId`, `DivisionId[]`, snapshotted profile, `PaymentStatus {Paid, Owed, Waived}` | snapshot of profile at registration time |
| **Bracket** | `BracketId`, `DivisionId`, `format`, `Seed[]`, `Match[]`, `status` | projection-built; regenerable pre-start |
| **Match** | `MatchId`, `BracketId`, `roundIndex`, `slotIndex`, `competitorA/B` (Registration refs or Bye), `MatchOutcome?`, `matAssignment?` | |
| **WeighIn** | `WeighInId`, `AthleteId`, `DivisionId`, `recordedWeight`, `WeighInOutcome`, `Recommendation?` | immutable; corrections are new events |
| **CheckIn** | `CheckInId`, `AthleteId`, `EventId`, `at` | append-only |
| **DeviceCredential** | `DeviceId`, `EventId`, `Role {Judge(mat), CheckIn}`, `WorkerId`, `revoked` | issued at pairing |
| **PaymentRecord** | `PaymentId`, `RegistrationId`, `method {AtDoor, Card}`, `state` | card path via stub (U8) |

### Value objects
| VO | Shape | Rules driven by |
|---|---|---|
| **DivisionCriteria** | weight range, rank range, age range, gender | FR-3.1 assignment |
| **WeightClass** | `{ lowerBound?, upperBound }` | Q6 tolerance is % of `upperBound` |
| **WeighInPolicy** | `{ mode: Strict \| AutoMove \| Tolerance, tolerancePercent? }` | Q6=A |
| **ScoringConfig** | `{ pointSparring: PointSparringConfig, forms: FormsConfig }` | per event |
| **PointSparringConfig** | `{ targetScore?: int, mercyGap?: int, penaltyPolicy: PenaltyPolicy }` | Q1=A, Q2=D |
| **PenaltyPolicy** | `{ mode: AwardOpponent \| DeductOffender, cap: int, capAction: Disqualify }` | Q2=D (configurable) |
| **FormsConfig** | `{ dropHighLowWhenJudges≥: 5, tieBreak: [HighestRemaining, Runoff] }` | Q3=A |
| **Seed** | `{ Registration, seedNumber }` | Q5 seeding |
| **ScoreEntry** | `{ judgeDeviceId, competitor, value }` | forms per-judge scores |
| **MatchOutcome** | `{ winner, method {Points, Forfeit, DQ, Decision}, detail }` | FR-6.3 |
| **WeighInOutcome** | `{ result: Pass \| Dq \| Moved \| TolerancePass, targetDivisionId? }` | Q6 / FR-5.3 |
| **Recommendation** | `{ suggested: WeighInPolicy.mode-value, byDeviceId }` | D-25 (non-binding) |

### Key relationships (text)
```
EventDefinition 1─* Division 1─* Registration *─1 AthleteProfile
Division 1─1 Bracket 1─* Match
EventDefinition 1─* OrganizerRoleAssignment
EventDefinition 1─* DeviceCredential
Registration 1─* WeighIn / CheckIn / PaymentRecord
```

### Cross-entity invariants (asserted by PBT — see business-rules.md)
1. Every event has ≥1 `OrganizerRoleAssignment` with `Role=FullAdmin`.
2. A `Registration` appears at most once per `Division` (idempotent registration).
3. A `Bracket` contains each participating `Registration` exactly once (participant preservation).
4. A completed single-elim `Bracket` yields exactly one winner.
5. `WeighIn`/`CheckIn`/`Score` are append-only; no entity is mutated in place.
