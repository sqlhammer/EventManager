# U3 Cloud Backend — Business Logic Model

**Stage**: CONSTRUCTION → Functional Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Technology-agnostic flows for the 20 primary stories.

**Universal write path** (from `services.md` cross-cutting principle): every domain mutation follows
**`validate → durable event append (AppendIfNotExists) → project → respond`**. No response precedes durability. The Identity plane (auth) is the only exception — it uses ASP.NET Identity's own transactional writes.

Legend: **VP** = validation pass, **W** = event write, **P** = projection update.

---

## Flow group A — Accounts & Auth (Identity plane; US-101/102/103)

### A1. Register account — `AccountController.Register` (US-101)
1. **VP**: email well-formed & not already used (respond with a **generic** non-enumerating message on duplicate, BR-AUTH-3); password ≥ 8 chars **and** passes breached-password check (BR-AUTH-1).
2. Create Identity user (`EmailConfirmed = false`); mint cloud `Snowflake` account id.
3. Issue `EmailConfirmationToken`; hand to `IEmailSender` (stub logs token, Q5=A).
4. Respond `AccountResponse` — account exists but **cannot create an event until confirmed** (BR-AUTH-4).

### A2. Confirm email
1. Redeem token (unexpired, unconsumed); set `EmailConfirmed = true`; mark token consumed.

### A3. Login — `AccountController.Login` (US-102)
1. **VP**: credentials valid. On failure increment lockout counter; **progressive lockout** after threshold (BR-AUTH-2). Failure message is generic (BR-AUTH-3).
2. If `MfaEnabled`: return an **MFA challenge**; require TOTP before issuing tokens (BR-AUTH-5).
3. On success: issue **JWT access token** (server-validated every request, NFR-2.5) + refresh path.
4. Logout invalidates the refresh/session path (BR-AUTH-6).

### A4. MFA enroll/disable (US-103)
- **Enroll**: generate TOTP secret → QR + one-time **recovery codes**; verify a probe code before enabling.
- **Disable**: only after **re-authentication** (BR-AUTH-7).

---

## Flow group B — Event & Division setup (domain plane; US-104/105/106/107)

### B1. Create event — `EventController.Create` (US-104)
1. **Precondition**: caller's account `EmailConfirmed` (BR-AUTH-4).
2. **VP**: required fields present; **dates ordered** (registration window start ≤ end; event date coherent); **fees ≥ 0** (BR-EVT-1).
3. **W**: `EventCreated` + `OrganizerAssigned(creator, FullAdmin)` (D-20) — appended together so a created event always has an owner (BR-EVT-2).
4. **P**: `EventProjection`, `OrganizerProjection`. Respond `EventResponse`.

### B2. Edit event — `EventController.Update` (US-104, Q4=A)
1. Authz: caller has an organizer assignment on the event (any role — editing details is not Full-Admin-only).
2. **VP**: **which fields are editable is governed by `RegistrationStatus`** (BR-EVT-3): before `Open`, all fields editable; after `Open`, restricted set + edits are visibly "recorded changes." Same **persistence path either way** (Q4=A) — always a `EventDetailsChanged` event.
3. **W** `EventDetailsChanged` → **P**.

### B3. Configure weigh-in policy (US-105)
1. **VP**: exactly one policy mode; `Tolerance` mode **requires** `TolerancePercent` (BR-EVT-4). Reject if event-day check-in has begun (policy locks — BR-EVT-5; in U3 this is a guard, enforced fully once hub reports check-in start).
2. **W** `EventDetailsChanged` (policy sub-field) → **P**.

### B4. Configure divisions — `EventController`/division endpoints (US-106)
1. **VP**: ranges valid; **no overlap** within the same gender/rank/age slice unless explicitly allowed (BR-DIV-1). Per-division bracket-format default derived from expected size, overridable (FR-3.2).
2. **Clone** path: copy divisions from a prior event the caller organizes (`DivisionCloned`).
3. **W** `DivisionConfigured` / `DivisionCloned` → **P** `DivisionProjection`.

### B5. Configure payment options (US-107)
1. **VP**: `AtDoor` always enabled; `Card` enabled **only** when a provider is configured (BR-PAY-1). No live charges — U8 stub (D-06).
2. **W** `EventDetailsChanged` (payment options) → **P**.

---

## Flow group C — Organizer RBAC (domain plane; US-108/109)

### C1. Add co-organizer — `OrganizerController.AddOrganizer` (US-108)
1. **Authz**: caller must be **Full Admin** on the event (BR-RBAC-1); Co-Organizer attempt → `403` + audit (NFR-2.11).
2. Two sub-paths (D-21):
   - **By existing account** (direct add): look up organizer account → **W** `OrganizerAssigned(CoOrganizer)` immediately.
   - **By email invite**: create `OrganizerInvitation(Pending)` → `IEmailSender` (stub). Invitee without an account is prompted to create one first; on **accept** → **W** `OrganizerAssigned(CoOrganizer)`.
3. No cap on organizers per event (D-22). Default role = Co-Organizer.
4. **P** `OrganizerProjection`.

### C2. Change role / Full-Admin-only actions — `OrganizerController.ChangeRole` (US-109)
1. **Authz**: **Full Admin only** for elevate/demote/remove/delete (`RoleAuthorizationPolicy.IsPermitted` over `OrganizerAction.{DemoteOrganizer, RemoveOrganizer, TransferFullAdmin, DeleteEvent}`) — the **same U1 policy instance** the hub uses (BR-RBAC-2).
2. **Last-admin guard**: a Full Admin cannot demote/remove themselves (or the last Full Admin) unless another Full Admin remains (BR-RBAC-3).
3. **W** `OrganizerRoleChanged` / `OrganizerRemoved` → **P**. All authz decisions enforced **server-side** (NFR-2.5); denials logged (NFR-2.11).

---

## Flow group D — Registration (domain plane; US-201–207, 209–211)

### D1. Create/edit athlete profile (US-201/203)
1. **VP**: DOB plausible; weight within global bounds; academy free-text/picklist (BR-REG-1).
2. **W** `AthleteProfileCreated/Updated`; record `AthleteProfileOwnership(OwnerAccountId)`. Parent accounts own multiple minor profiles (US-203). Edits are versioned (BR-REG-2).

### D2. Division eligibility computation (shared sub-routine, Q3=A; US-210)
Given a profile snapshot + the event's divisions:
1. Run U1 division-assignment logic: a division is **eligible** iff snapshot matches `DivisionCriteria` (weight ∈ WeightClass, rank ∈ RankRange, age ∈ AgeRange, gender match).
2. Return the **eligible set** (offered to the registrant) + a **default selection**. Non-matching divisions are **not offered** (BR-REG-3).
This routine is deterministic (PBT invariant, §PBT-1).

### D3. Self / parent registration — `RegistrationController.Register` (US-202/203/207/210)
1. **Precondition**: registration window `Open` for the event (BR-REG-4).
2. **VP**: profile complete; compute eligible divisions (D2); registrant's selected divisions ⊆ eligible set.
3. Compute **fee total** = entry fee × division count (per event fee model); payment election:
   - **At-door** (US-207): status `Owed`, balance visible to registrant + organizer.
   - **Card** (US-208/U8): call `IPaymentProvider.ChargeAsync`; success → `Paid`, decline/timeout → `Owed` + retry handle.
4. **W** `RegistrationSubmitted` (+ `PaymentRecorded`), `ManagedByAccountId` = parent for minors (US-203).
5. **P** `RosterProjection`. Respond confirmation + fee summary.

### D4. Coach bulk registration — `RegistrationController.RegisterBatch` (US-205/206, Q2=A)
**Two-phase, atomic (Q2=A):**
1. **Phase 1 — validation pass over the whole batch**: for each selected athlete, compute eligibility (D2), detect conflicts: already-registered for the same division (idempotent guard, BR-REG-5), ineligible for a chosen division, incomplete profile.
2. **If any entry fails** → **commit nothing**; respond an **itemized report** (per-athlete reasons). Coach fixes flagged entries and **resubmits** (BR-REG-6).
3. **If all pass** → **W** all `RegistrationSubmitted` events under **one batch idempotency key** (resubmit with the same key is a no-op for already-committed entries — BR-REG-7); one **combined fee summary**.
4. **P** `RosterProjection`. No athlete double-registered for a division (PBT invariant, §PBT-2).

### D5. Edit / withdraw registration — `RegistrationController.Edit/Withdraw` (US-211)
1. **Authz**: caller is the managing account (self or parent) **and** window `Open`; after close → organizer-only (routes to D7).
2. **VP** + re-run eligibility (D2) on changed division/weight → recompute fees.
3. **W** `RegistrationEdited` (re-snapshots profile) / `RegistrationWithdrawn`; if a confirmed division no longer matches → also `DivisionAssignmentChanged` and set the **mismatch flag** for organizer review (US-210).
4. **P**.

### D6. Automatic division assignment on submit/correct (US-210)
Not a separate endpoint — invoked inside D3/D4/D5/D7. Assignment matching the profile is applied; mismatches surface to the organizer via the roster mismatch flag rather than silently failing.

### D7. Organizer roster management — `RegistrationController`/roster endpoints (US-209)
1. **Authz**: any organizer on the event (not Full-Admin-only) — `OrganizerAction.ManageRoster`.
2. Actions: approve / withdraw / correct a registration; **corrections re-run assignment** (D2) and are recorded events; mark `Owed` balance **Paid** (cash) or **Waived** (BR-PAY-2).
3. Roster read is filterable by division / academy / payment status (`RosterProjection`).
4. **W** appropriate correction events → **P**.

---

## Flow group E — Replication ingest & results (US-504 ingest side, US-603)

### E1. Ingest replicated batch — `EventIngestController.IngestBatch` (US-504, Q7=A)
1. **Authz (Q7=A)**: `[Authorize]` JWT; caller is an **organizer-scoped service principal** authorized for the batch's `EventScopeId` (event-scoped RBAC). Foreign-event ingest → `403` (BR-ING-1).
2. Order the batch by `SequenceNumber`; for each event `AppendIfNotExistsAsync` — **idempotent**: replays after an outage never duplicate (BR-ING-2). The cloud never conflicts with the hub — it is a mirror.
3. **P**: incremental projection dispatch (incl. `ResultsProjection`). Respond `IngestResult { accepted, duplicatesSkipped, highWaterMark }` so the hub resumes from the last acked sequence with no gaps.
4. Bounded, resumable — the endpoint is safe under retry/backoff (NFR-3.8; full replication resiliency owned by U7).

### E2. Registrant results & history — `ResultsController.GetForAthlete` (US-603, Q6=A)
1. **Authz**: caller owns/manages the `AthleteId` (object-level authz).
2. Read `ResultsProjection` keyed by athlete → `ResultsResponse` (events entered, divisions, placements, W-L). **Empty/partial until real event-day events are ingested** — schema stable now (Q6=A), no rework when U4b/U7 land.

---

## Sequence sketch — self-registration (representative)

```
Registrant → RegistrationController.Register(req)
  1 window Open?                         (BR-REG-4)  ── no ─▶ 409 window closed
  2 profile valid? eligible divisions ⊇ selected?     ── no ─▶ 422 itemized
  3 fee total; payment election
        at-door ─▶ status Owed
        card    ─▶ IPaymentProvider.ChargeAsync ─▶ Paid | Owed+retry
  4 AppendIfNotExists(RegistrationSubmitted, PaymentRecorded)   ◀ durable
  5 project RosterProjection
  6 200 confirmation + fee summary
```

---

## PBT invariants surfaced for the NFR/testing stages (blocking extension)
- **§PBT-1 Division-assignment determinism**: same profile + same divisions ⇒ same eligible set, any order.
- **§PBT-2 No double-registration**: for any batch (incl. resubmits with the same key), no athlete is registered twice for one division.
- **§PBT-3 RBAC deny-by-default**: no non-Full-Admin caller can perform any `FullAdminOnly` action; no caller without an assignment performs any organizer action.
- **§PBT-4 Ingest idempotency**: ingesting any batch, in any partition/order, any number of times ⇒ identical resulting log & projection.

## Story → flow traceability
US-101→A1/A2 · US-102→A3 · US-103→A4 · US-104→B1/B2 · US-105→B3 · US-106→B4 · US-107→B5 · US-108→C1 · US-109→C2 · US-201→D1 · US-202→D3 · US-203→D1/D3 · US-204→(AcademyRoster CRUD, D-adjacent) · US-205→D4 · US-206→D4 · US-207→D3 · US-209→D7 · US-210→D2/D6 · US-211→D5 · US-603→E2 · (US-504 ingest)→E1.
