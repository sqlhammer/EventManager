# U3 Cloud Backend — Business Rules

**Stage**: CONSTRUCTION → Functional Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Enumerated, testable rules referenced by `business-logic-model.md`.

Each rule: **ID · statement · source story/NFR · enforcement point**. Security-sensitive rules are tagged **🔒** and carried forward to NFR Requirements. PBT-relevant rules tagged **⊚**.

---

## Auth & account (Identity plane)

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-AUTH-1** 🔒 | Password ≥ 8 chars **and** passes a breached-password check before acceptance. | US-101, NFR-2.6 | A1 register |
| **BR-AUTH-2** 🔒 | Repeated failed logins trigger **progressive lockout** (increasing delay/threshold). | US-102, NFR-2.6 | A3 login |
| **BR-AUTH-3** 🔒 | Duplicate-email and failed-login responses are **generic / non-enumerating** — never reveal whether an email exists. | US-101/102 | A1, A3 |
| **BR-AUTH-4** | An account cannot create an event until its email is **confirmed**. | US-101/104 | B1 precondition |
| **BR-AUTH-5** 🔒 | When MFA is enabled, a valid **TOTP** is required before tokens are issued. | US-103 | A3 |
| **BR-AUTH-6** 🔒 | Logout invalidates the session/refresh path. | US-102 | A3 logout |
| **BR-AUTH-7** 🔒 | MFA can be disabled **only after re-authentication**. | US-103 | A4 |
| **BR-AUTH-8** 🔒 | Every authenticated API call validates the JWT server-side. | US-102, NFR-2.5 | all controllers |

## Event & division setup (domain plane)

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-EVT-1** | Event requires all fields; **dates ordered** (window start ≤ end; coherent event date); **fees ≥ 0**. | US-104 | B1 VP |
| **BR-EVT-2** | Creating an event atomically assigns the creator **Full Admin** (event never exists without an owner). | US-104, D-20 | B1 W |
| **BR-EVT-3** | Editable fields are governed by `RegistrationStatus`: before `Open`, all editable; after `Open`, a restricted set, and edits are recorded (visible) changes. Persistence path is always an event (Q4=A). | US-104 | B2 VP |
| **BR-EVT-4** | Exactly one weigh-in policy mode active per event; `Tolerance` mode **requires** a percentage. | US-105 | B3 VP |
| **BR-EVT-5** | Weigh-in policy **locks** once event-day check-in begins. | US-105 | B3 VP (guarded; fully enforced once hub reports check-in start) |
| **BR-DIV-1** ⊚ | Division ranges must not **overlap** within the same gender/rank/age slice unless explicitly allowed. | US-106 | B4 VP |
| **BR-DIV-2** | Per-division bracket-format default derived from expected size, organizer-overridable. | US-106, FR-3.2 | B4 |
| **BR-DIV-3** | Divisions clonable only from an event the caller organizes. | US-106 | B4 clone |

## Organizer RBAC (reuses U1 `RoleAuthorizationPolicy`)

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-RBAC-1** 🔒 | Only a **Full Admin** may add/invite another organizer; Co-Organizer attempt → authorization error + audit. | US-108 | C1 authz |
| **BR-RBAC-2** 🔒⊚ | Full-Admin-only actions (`DeleteEvent, RemoveOrganizer, DemoteOrganizer, TransferFullAdmin`) are gated by the **same U1 `RoleAuthorizationPolicy` instance** used on the hub — authz cannot diverge cloud vs hub. Deny-by-default when no assignment. | US-109, FR-2.8, NFR-2.5 | C2 authz |
| **BR-RBAC-3** | A Full Admin cannot demote/remove themselves or the **last** Full Admin unless another Full Admin remains on the event. | US-109 | C2 |
| **BR-RBAC-4** | New organizers default to **Co-Organizer**; no cap on organizers per event. | US-108, D-21/D-22 | C1 |
| **BR-RBAC-5** | Co-Organizers may perform all non-Full-Admin-only event actions identically to Full Admin. | US-109, FR-2.8 | all organizer actions |
| **BR-RBAC-6** 🔒 | Every authorization **denial** is logged (audit trail). | US-109, NFR-2.11 | policy call sites |

## Registration (domain plane)

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-REG-1** | Athlete profile fields validated: DOB plausible, weight within global bounds, academy free-text/picklist. | US-201 | D1 VP |
| **BR-REG-2** | Profile edits are versioned; a registration **snapshots** profile data at registration time and is not retroactively changed by later profile edits. | US-201 | D1/D3 |
| **BR-REG-3** ⊚ | Only divisions whose criteria the profile matches are **offered**; a submitted selection must be a subset of the eligible set (Q3=A). | US-202/210, FR-3.1 | D2/D3 VP |
| **BR-REG-4** | Registration (self/parent/coach) is permitted only while the event's window is **Open**; after close, edits require the organizer. | US-202/211 | D3/D5 |
| **BR-REG-5** ⊚ | No athlete is registered twice for the **same division** (idempotent). | US-206 | D3/D4 |
| **BR-REG-6** | Bulk registration is **atomic**: any conflict fails the whole batch with an **itemized** report; nothing commits until all entries pass (Q2=A). | US-205/206 | D4 |
| **BR-REG-7** ⊚ | A batch carries an **idempotency key**; resubmitting the same key does not double-register already-committed entries. | US-206 | D4 |
| **BR-REG-8** | A minor's registration is **managed by** the parent account (edit/withdraw), though it belongs to the child's profile. | US-203 | D3/D5 authz |
| **BR-REG-9** | Editing division/weight or withdrawing **re-runs division assignment** and recomputes fees. | US-211/210 | D5 |
| **BR-REG-10** | A confirmed division that no longer matches after an edit is **flagged to the organizer** (mismatch), not silently dropped. | US-210/211 | D5/D7 |
| **BR-REG-11** | Coach academy roster is **coach-owned**; object-level authorization enforced on all roster ops. | US-204, NFR-2.5 | roster CRUD |

## Payments (status projection; charging delegated to U8)

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-PAY-1** | Pay-at-door is **always** available; the card path is offered **only** when a provider is configured; MVP uses the U8 stub (no live charges). | US-107/207, D-06 | B5/D3 |
| **BR-PAY-2** | Organizer may transition an `Owed` balance to **Paid** (cash) or **Waived**; transitions are recorded events. | US-209 | D7 |
| **BR-PAY-3** | Card success → `Paid`; provider decline/timeout leaves the registration **`Owed` (unpaid-but-held)** with a clear retry path. | US-208, D-06 | D3 (via U8) |

## Replication ingest & results

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-ING-1** 🔒 | Ingest is authenticated (JWT) and authorized to the batch's **`EventScopeId`** (event-scoped organizer service principal, Q7=A); foreign-event ingest rejected. | US-504 | E1 authz |
| **BR-ING-2** ⊚ | Ingest is **idempotent** and sequence-ordered per device; replays never duplicate; the cloud is a mirror and never conflicts with the hub. | US-504, FR-4.6 | E1 |
| **BR-ING-3** | `ResultsProjection` folds only its known result-event subset; unknown ingested event types are ignored (forward-compatible with U4b). | US-603, Q6=A | E1/E2 |
| **BR-RES-1** 🔒 | Results are readable only by an account that owns/manages the athlete (object-level authz). | US-603, NFR-2.5 | E2 |

## Cross-cutting

| ID | Rule | Source | Enforced at |
|---|---|---|---|
| **BR-X-1** | Every domain mutation is an immutable event; read models are projections — services never mutate read models directly. | services.md, Q1=C | all W/P |
| **BR-X-2** | Snowflake IDs for cloud-owned entities mint in the **cloud worker range**; they travel unchanged through replication. | Q8/Q10 | all W |
| **BR-X-3** 🔒 | All API request models are validated (U2 `Contracts` validators / Data Annotations) **before** reaching the event-write path. | NFR-2.4 | all controllers |
| **BR-X-4** | Email confirmation and organizer invitations go through the `IEmailSender` seam; MVP stub records the token (Q5=A). | US-101/108 | A1/C1 |

---

## PBT invariants (blocking Property-Based-Testing extension)

| ID | Invariant | Backed by rules |
|---|---|---|
| **PBT-1** | Division-assignment is deterministic & order-independent for a given profile + division set. | BR-REG-3, BR-DIV-1 |
| **PBT-2** | No athlete is ever double-registered for a division, across any batch or resubmission. | BR-REG-5, BR-REG-7 |
| **PBT-3** | RBAC is deny-by-default: no caller lacking the required role/assignment performs a gated action. | BR-RBAC-2 |
| **PBT-4** | Ingest is idempotent: any batch, any order/partition, any repetition ⇒ identical log + projections. | BR-ING-2 |

## Security-sensitive rules → NFR Requirements stage (Security Baseline, blocking)
BR-AUTH-1..8, BR-RBAC-1/2/6, BR-ING-1, BR-RES-1, BR-X-3 — carry into NFR Requirements for concrete controls (breached-password source, lockout parameters, JWT lifetime/rotation, TOTP standard, audit-log destination, object-level authorization pattern, input-validation strategy).

## Resiliency notes → NFR Design stage (Resiliency Baseline, blocking)
- Ingest must be safe under retry/backoff and resumable from last acked sequence (BR-ING-2; U7 owns full replication resiliency).
- Payment provider calls (U8) must tolerate decline/timeout without leaving inconsistent state (BR-PAY-3).
- Durable-before-respond on every domain write (BR-X-1).
