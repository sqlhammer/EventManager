# U3 Cloud Backend — Domain Entities & Read Models

**Stage**: CONSTRUCTION → Functional Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Technology-agnostic (no EF/SQL specifics — those land in NFR/Infra design)

Design decisions embedded here: **Q1=C** (hybrid persistence), **Q3=A** (eligibility + confirm), **Q6=A** (results projection now), **Q7=A** (event-scoped ingest auth). Reuses U1 `EventManager.Domain` entities verbatim where they exist — U3 adds cloud-only entities, the event vocabulary, and the read models.

---

## 1. Two persistence planes (Q1=C)

U3 holds **two** kinds of state, deliberately separated:

| Plane | What lives here | Source of truth | Mutability |
|---|---|---|---|
| **Identity plane** | `Account` credentials, password hashes, MFA secrets, email-confirmation & lockout state | ASP.NET Identity tables | Mutable rows (standard Identity) — **never** event-sourced |
| **Domain plane** | Event, Division, Registration, OrganizerRoleAssignment, WeighInPolicy, PaymentStatus, and all ingested event-day state | Append-only `IEventStore` log (`TournamentEvent`) | Immutable events; current state is a **projection** |

**Rule:** the domain plane never mutates a row in place. Every create/edit/withdraw/correction is a new `TournamentEvent` appended via `PostgresEventStore.AppendIfNotExistsAsync`; read models are folded from the log (US-104/209/211 "recorded as events"). The Identity plane is conventional because credentials are not domain history and must support Identity's own security machinery (breached-password check, lockout counters, TOTP).

**Bridge between planes:** `Account.Id` (a Snowflake minted in the cloud worker range) is the stable key referenced by domain events (`OrganizerRoleAssignment.AccountId`, registration ownership). Identity's own GUID/string user-id is an internal detail of the Identity plane.

---

## 2. Identity-plane entities (conventional)

### 2.1 `Account`
The person-level login. One account, many capabilities (an account may be organizer **and** coach **and** parent — capability is contextual, not a rigid role).

| Field | Type | Notes |
|---|---|---|
| `Id` | `Snowflake` | Cloud-minted; the cross-plane key |
| `Email` | string (unique, normalized) | Login identifier |
| `PasswordHash` | Identity-managed | min 8 chars + breached-password check (US-101, NFR-2.6) |
| `EmailConfirmed` | bool | Gates first event creation (US-101) |
| `MfaEnabled` | bool | TOTP (US-103) |
| `MfaSecret` / `RecoveryCodes` | Identity-managed (encrypted) | Enrollment via QR (US-103) |
| `LockoutState` | Identity-managed | Progressive lockout (US-102, NFR-2.6) |

**Capabilities are not stored as a fixed role column.** Organizer authority is expressed per-event via `OrganizerRoleAssignment` (domain plane). Coach ownership is expressed via `AcademyRoster` ownership. Parent/registrant is expressed by owning `AthleteProfile`s. This keeps one account flexible across the four narrative personas (organizer, coach, parent, registrant).

### 2.2 `EmailConfirmationToken` / `OrganizerInvitation` (Q5=A)
Backed by the `IEmailSender` stub seam.

| Entity | Fields | Purpose |
|---|---|---|
| `EmailConfirmationToken` | `AccountId`, `Token`, `ExpiresAt`, `ConsumedAt?` | US-101 confirmation |
| `OrganizerInvitation` | `Id`, `EventId`, `InviterAccountId`, `InviteeEmail`, `Token`, `Status {Pending,Accepted,Expired}`, `ExpiresAt` | US-108 invite-by-email; on accept → emits `OrganizerAdded` domain event |

---

## 3. Domain-plane entities (event-sourced projections)

These reuse U1 `EventManager.Domain` record types as the **projection shape**. The event log is the writer; these are the read-model rows that projections maintain.

### 3.1 `EventDefinition` *(reused from U1)*
Root aggregate for a tournament. Projection of `EventCreated` + `EventDetailsChanged` events.
- Fields (U1): `EventId, Name, Venue, Date, RegistrationWindow (DateRange), EntryFee, WeighInPolicy, ScoringConfig`.
- **U3 adds to the projection** (read-model columns, not new U1 record fields): `RegistrationStatus {Draft, Open, Closed}` derived from window vs. clock + explicit open/close events; `PaymentOptions` (see 3.6); `CreatedByAccountId`.
- Lifecycle: creator is auto-assigned Full Admin (US-104, D-20) via an accompanying `OrganizerAssigned` event.

### 3.2 `Division` *(reused from U1)*
Projection of `DivisionConfigured` / `DivisionUpdated` / `DivisionCloned` events.
- Fields (U1): `DivisionId, EventId, Criteria (DivisionCriteria: WeightClass/RankRange/AgeRange/Gender), Format (BracketFormat), Status (DivisionStatus)`.
- Pre-event, `Status = NotStarted`. U3 owns configuration only; bracket generation is U4b.

### 3.3 `AthleteProfile` *(reused from U1)*
Owned by an `Account` (self or parent-managed minors, US-201/203). Projection of `AthleteProfileCreated` / `AthleteProfileUpdated`.
- Fields (U1): `AthleteId, Name, DateOfBirth, Rank, Weight, Academy, Gender`.
- **Ownership edge** (U3 read model): `AthleteProfileOwnership { AthleteId, OwnerAccountId }` — a parent account owns multiple minor profiles (US-203); object-level authz keys off this.
- Profile edits are versioned; a `Registration` snapshots the profile **at registration time** (US-201) — see 3.4.

### 3.4 `Registration` *(reused from U1)*
Projection of `RegistrationSubmitted` / `RegistrationEdited` / `RegistrationWithdrawn` / `DivisionAssignmentChanged` / `PaymentStatusChanged`.
- Fields (U1): `RegistrationId, EventId, AthleteId, DivisionIds (list), Snapshot (AthleteProfile), PaymentStatus`.
- **Snapshot semantics:** `Snapshot` freezes the athlete's profile at submit time; later profile edits do not retroactively change a registration (US-201). An explicit `RegistrationEdited` re-snapshots and re-runs assignment (US-211).
- **Managed-by edge:** `ManagedByAccountId` — for minors this is the parent, not the athlete (US-203); governs who may edit/withdraw.
- **Assignment mismatch flag:** `HasAssignmentMismatch` (bool) + `MismatchReasons` — set when a confirmed division no longer matches the snapshot after an edit (US-210/211); surfaced to the organizer roster.

### 3.5 `OrganizerRoleAssignment` *(reused from U1)*
Projection of `OrganizerAssigned` / `OrganizerRoleChanged` / `OrganizerRemoved`.
- Fields (U1): `Id, EventId, AccountId, Role (OrganizerRole {FullAdmin, CoOrganizer})`.
- The authoritative input to `RoleAuthorizationPolicy` (U1) for every organizer action (S-2). Deny-by-default when no assignment exists.

### 3.6 `PaymentOptions` (per-event) + payment status (US-107/207)
- `PaymentOptions { AtDoorEnabled (always true), CardEnabled (only if provider configured) }` — projection field on the event.
- Registration `PaymentStatus` (U1 enum `{Paid, Owed, Waived}`) transitions are events: `PaymentRecorded`, `BalanceMarkedPaid`, `BalanceWaived` (US-207/209). Card path delegates to U8 `IPaymentProvider`; a successful `PaymentResult` emits `PaymentRecorded(Paid)`, a decline/timeout leaves `Owed` with a retry handle (US-208 is U8's story but U3 owns the status projection).

### 3.7 `AcademyRoster` (coach-owned, US-204)
- `AcademyRoster { RosterId, OwnerAccountId, AcademyName }` + `RosterMember { RosterId, AthleteId, AddedVia {Manual, InviteAccepted} }`.
- Coach-owned; object-level authz (NFR-2.5). Feeds bulk registration (US-205).

---

## 4. Read models / projections owned by U3

| Projection | Folds these events | Serves |
|---|---|---|
| `EventProjection` | EventCreated, EventDetailsChanged, Registration(Open/Close) | US-104 event read, registration-window gating |
| `DivisionProjection` | DivisionConfigured/Updated/Cloned | US-106 division list, eligibility source |
| `RosterProjection` | RegistrationSubmitted/Edited/Withdrawn, DivisionAssignmentChanged, PaymentStatusChanged | US-209 roster (filter by division/academy/payment status), US-210 mismatch surfacing |
| `OrganizerProjection` | OrganizerAssigned/RoleChanged/Removed | S-2 RBAC lookups, US-108/109 organizer list |
| `ResultsProjection` (Q6=A) | ingested event-day events: `MatchCompleted`, `DivisionFinalized`, `BracketAdvanced` | US-603 registrant results & history — empty until U4b/U7 replicate real events |

**ResultsProjection contract (Q6=A):** defined now so ingest and the read API are schema-stable. Keyed by `AthleteId`; rows: `{ EventId, EventName, Date, DivisionId, Placement?, Record {W-L}, Status }`. Folds a small, explicit set of ingested result event types; unknown event types are ignored by this projection (forward-compatible with U4b's full vocabulary).

---

## 5. Event vocabulary owned/emitted by U3

Domain-plane events U3 **writes** (cloud worker as `DeviceId`, `EventScopeId` = the tournament event's Snowflake, enabling event-scoped partitioning + ingest authz per Q7):

`EventCreated`, `EventDetailsChanged`, `RegistrationOpened`, `RegistrationClosed`,
`DivisionConfigured`, `DivisionUpdated`, `DivisionCloned`,
`AthleteProfileCreated`, `AthleteProfileUpdated`,
`RegistrationSubmitted`, `RegistrationEdited`, `RegistrationWithdrawn`, `DivisionAssignmentChanged`,
`PaymentRecorded`, `BalanceMarkedPaid`, `BalanceWaived`,
`OrganizerAssigned`, `OrganizerRoleChanged`, `OrganizerRemoved`,
`AcademyRosterCreated`, `RosterMemberAdded`, `RosterMemberRemoved`.

Events U3 **ingests** (does not author — written by hub/spokes, replicated in via S-7): the full event-day vocabulary; U3's projections consume only the subset relevant to `ResultsProjection`.

**Payload/serialization:** each event's `Payload` is serialized via U2 `EventManager.Contracts` (`EventEnvelopeMapper`) — the same wire format used for replication, so cloud-authored and hub-replicated events share one envelope. `SchemaVersion` per event type supports upcasting (U1 `IUpcaster`).

---

## 6. Entity relationship summary (text)

```
Account (Identity plane)
  ├─ owns → AthleteProfile*        (self + minors; AthleteProfileOwnership)
  ├─ owns → AcademyRoster*         (coach capability)
  └─ referenced by ↓ (Snowflake Id bridges to domain plane)

EventDefinition (domain, event-sourced)
  ├─ has → Division*               (DivisionCriteria)
  ├─ has → OrganizerRoleAssignment*  → Account   (RBAC, US-108/109)
  ├─ has → PaymentOptions
  └─ has → Registration*
             ├─ for → AthleteProfile (Snapshot frozen at submit)
             ├─ managed-by → Account (parent for minors)
             ├─ assigned → Division* (eligibility-filtered, Q3)
             └─ PaymentStatus {Paid, Owed, Waived}

ResultsProjection (read model)
  └─ keyed-by AthleteId, folded from ingested event-day events
```

---

## 7. Traceability

| Story | Entities / projections |
|---|---|
| US-101/102/103 | `Account`, `EmailConfirmationToken` (Identity plane) |
| US-104 | `EventDefinition` + EventCreated/EventDetailsChanged; auto `OrganizerAssigned(FullAdmin)` |
| US-105 | `WeighInPolicy` on event (U1 type) |
| US-106 | `Division` + DivisionConfigured/Cloned |
| US-107/207 | `PaymentOptions`, `PaymentStatus` transitions |
| US-108/109 | `OrganizerInvitation`, `OrganizerRoleAssignment` + RBAC events |
| US-201/203 | `AthleteProfile`, `AthleteProfileOwnership`, managed-by edge |
| US-202/210/211 | `Registration`, `DivisionAssignmentChanged`, mismatch flag |
| US-204/205/206 | `AcademyRoster`, `RosterMember`, batch registration events |
| US-209 | `RosterProjection` (filters), payment status events |
| US-603 | `ResultsProjection` |
