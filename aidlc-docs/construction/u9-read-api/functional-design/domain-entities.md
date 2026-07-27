# Domain Entities — Unit U9: Read/Query API

**Stage**: CONSTRUCTION → Functional Design
**Answers**: Q1=A (no ETag on registrant detail) · Q2=A (deleted-account registrations shown normally) · Q3=A (drop the soft-deleted-accounts clause) · Q4=C (status-only discovery, window surfaced)

---

## 1. This unit persists nothing

U9 introduces **no new entity, no event type, no projection, and no migration**. It is a read
surface over read-model rows that already exist and are already populated by
`CloudProjectionHost`. Every entity below is consumed read-only.

| Row consumed | Source of truth | Used by |
|---|---|---|
| `EventRow` | EventProjection | Event summary/detail, T0 qualification, weigh-in policy |
| `DivisionRow` | DivisionProjection | Division endpoints |
| `RegistrationRow` | RosterProjection | Registrant endpoints, T1 qualification |
| `AthleteProfileRow` | RosterProjection | Registrant **detail** only (date of birth, weight, rank, gender) |
| `OrganizerRow` | OrganizerProjection | Account endpoints, T2 qualification |
| `EventRecord` | Append-only event log | ETag watermark only — never projected into a response body |

---

## 2. Access tier (new value object)

```text
AccessTier = None | Public | Registrant | Organizer
```

Totally ordered, cumulative: `None < Public < Registrant < Organizer`. A caller holds exactly one
tier per event — the highest they qualify for — and that tier confers every lower tier's grants.

| Tier | Qualification | Grants |
|---|---|---|
| `None` | qualifies for nothing | no data at all; every request 404s |
| `Public` | `EventRow.RegistrationStatus == Open` | event **summary**, divisions, weigh-in policy |
| `Registrant` | a non-withdrawn `RegistrationRow` with `ManagedByAccountId == caller` | + event **detail**, + own registration detail |
| `Organizer` | an `OrganizerRow` for `(eventId, caller)` | + full registrant roster, + organizer roster |

**Tier is per event.** The same caller is `Organizer` on events they run and `Public` on a stranger's
open event. Nothing is global.

**`OrganizerRole` does not refine read access.** Full Admin and Co-Organizer both resolve to
`Organizer` and receive byte-identical responses (US-703). Role-based redaction was considered and
declined at requirements (Q6=B).

---

## 3. Ownership invariant underpinning the Registrant tier

`RegistrationRow.ManagedByAccountId` is **always equal** to the `AthleteProfileRow.OwnerAccountId`
of the athlete it names. Both write paths enforce it as BR-REG-8:

- [`RegisterAsync`](../../../../backend/EventManager.Api/Services/RegistrationService.cs#L58) — rejects unless `profile.OwnerAccountId == callerAccountId`
- [`RegisterBatchAsync`](../../../../backend/EventManager.Api/Services/RegistrationService.cs#L87) — rejects unless `profile.OwnerAccountId == coachAccountId`

So T1 resolution can key on `ManagedByAccountId` alone with no ambiguity about who "owns" an entry.

⚠️ **Revisit if athlete-profile ownership transfer is ever added.** The moment ownership can move
independently of a registration, these two columns diverge and T1 must decide which one governs.

---

## 4. Response shapes

Shape is a function of resolved tier, not of endpoint.

### Event summary — `Public`
`eventId · name · venue · date · registrationStart · registrationEnd · entryFee · registrationStatus`

The registration window is present at the public tier by design (**Q4=C**): discovery keys off
status alone, and the window is surfaced so a client can render an event as expired without the API
concealing it.

### Event detail — `Registrant`, `Organizer`
Summary **plus** `cardEnabled · checkInStarted · weighInPolicy · createdByAccountId`

### Event collection item
Summary **plus** `accessTier`, and `organizerRole` when the caller holds one. The tier tag tells a
client which events it may open for detail without probing.

### Division
`divisionId · weightLower · weightUpper · minRank · maxRank · minAge · maxAge · gender · format · status`
Identical in the collection and single form.

### Weigh-in policy
`mode` and, **only when `mode == Tolerance`**, `tolerancePercent`. Exactly one per event; there is
no collection form.

### Registrant list item — `Organizer`
`registrationId · athleteId · athleteName · academy · divisionIds · paymentStatus · hasAssignmentMismatch · withdrawn`

Carries **no** date of birth, weight, rank, or gender.

### Registrant detail — `Organizer`, or `Registrant` for own records
List item **plus** `dateOfBirth · weight · rank · gender`, sourced from `AthleteProfileRow`.

This is the only shape that reads data outside the event scope — the reason it is excluded from
ETag coverage (§ business-logic-model, ETag rules).

### Organizer account
`accountId · email · role`

Never any password hash, MFA secret, recovery code, or session token. Email is included because
Full Admins already administer co-organizers by email (US-108).

---

## 5. Entities deliberately not exposed

| Not exposed | Reason |
|---|---|
| `ResultRow` | Already served by the existing `GET /api/results/athletes/{athleteId}`; out of scope |
| `EventRecord` payloads | The event log is an internal mechanism, not a read surface |
| `IdempotencyKey`, `RefreshTokenRecord`, `EmailOutboxRecord` | Infrastructure tables with no user-facing meaning; `RefreshTokenRecord` and `EmailOutboxRecord` are credential-bearing |
| `AppUser` beyond `accountId`/`email` | Identity record; credentials and MFA state must never leave the server |
| `AthleteProfileRow` as a standalone resource | Only reachable through registrant detail, so every read is authorized against an event |
