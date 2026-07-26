# EventManager — User Stories (MVP)

**Organization**: Epic-based hybrid — epics follow the product's journey phases; each story is tagged with persona and requirement IDs.
**Acceptance criteria**: Given/When/Then for behavioral/event-day flows; checklists for CRUD/setup stories.
**Prioritization**: Delivery-dependency ordering (see "Dependency ordering" note per epic and §Ordering Summary at the end). No MoSCoW labels per user decision.
**Count**: 66 stories across 7 epics — 56 MVP (Epics 1–6) plus 10 in Epic 7 (unit U9 Read/Query API, added 2026-07-26).

Personas: P1 Organizer · P2 Coach · P3 Registrant · P4 Judge · P5 Check-In Staff (see `personas.md`)

---

# EPIC 1 — Pre-Event Setup
*Organizer prepares an event in the cloud before registration opens.*
**Dependency ordering**: E1 precedes all other epics; US-101/102 precede everything.

### US-101 — Organizer account registration (P1) [FR-1.1]
As an organizer, I want to create an account with email and password so that I can manage my tournaments.
- [ ] Account created via ASP.NET Identity; password policy: min 8 chars + breached-password check (NFR-2.6)
- [ ] Email confirmation required before first event can be created
- [ ] Duplicate email rejected with a generic (non-enumerating) message

### US-102 — Organizer login (P1) [FR-1.1]
As an organizer, I want to log in and receive a session so that subsequent API calls are authorized.
- [ ] Successful login issues a JWT validated server-side on every request (NFR-2.5)
- [ ] Failed logins trigger progressive lockout after repeated attempts (NFR-2.6)
- [ ] Logout invalidates the session/refresh path

### US-103 — Organizer MFA (P1) [FR-1.5]
As an organizer, I want to enable TOTP MFA so that my administrative account is protected.
- [ ] TOTP enrollment with QR + recovery codes; MFA challenged at login when enabled
- [ ] MFA can be disabled only after re-authentication

### US-104 — Create event (P1) [FR-2.1]
As an organizer, I want to create an event with name, date, venue, registration window, and entry fees so that athletes can register.
- [ ] Event created with all required fields validated (dates ordered, fees ≥ 0)
- [ ] Event is only visible/registerable within its registration window
- [ ] Organizer can edit event details until registration opens; edits after are recorded events
- [ ] Creating organizer is assigned **Full Admin** on the new event by default (FR-2.1, D-20)

### US-105 — Configure weigh-in policy (P1) [FR-2.1, FR-5.3]
As an organizer, I want to choose my event's missed-weight policy (strict DQ / auto-move / tolerance %) so that weigh-in exceptions are handled my way.
- [ ] Exactly one policy active per event; tolerance policy requires a percentage value
- [ ] Policy is locked once event-day check-in begins

### US-106 — Configure divisions (P1) [FR-3.1]
As an organizer, I want to define divisions by weight/rank/age/gender ranges so that athletes are grouped fairly.
- [ ] Division ranges validated (no overlaps within the same gender/rank/age slice unless explicitly allowed)
- [ ] Divisions clonable from a previous event
- [ ] Per-division bracket format default derived from expected size, overridable (FR-3.2)

### US-107 — Configure payment options (P1) [FR-2.4]
As an organizer, I want to choose which payment methods my event accepts (pay-at-door always; card if configured) so that registration matches how I actually collect money.
- [ ] Pay-at-door always available; card path shown only when provider configured
- [ ] Card provider is the stubbed/mocked abstraction in MVP (D-06) — no live charges

### US-108 — Add a co-organizer to an event (P1) [FR-2.7]
As a Full Admin organizer, I want to add another organizer to my event — by inviting a new person via email or adding an existing organizer account directly — so that I can share event administration.
- [ ] Full Admin can invite by email; invitee without an Organizer account is prompted to create one before the invite is accepted; invitee with an existing account accepts directly (D-21)
- [ ] Full Admin can add an existing Organizer-role account directly by lookup, no invite/accept step (D-21)
- [ ] Newly added organizer defaults to Co-Organizer role; no limit on the number of organizers per event (D-22)
- [ ] Only Full Admin organizers can add other organizers; attempt by a Co-Organizer is rejected with a clear authorization error

### US-109 — Manage organizer roles & Full-Admin-only actions (P1) [FR-2.8]
As a Full Admin organizer, I want to elevate/demote co-organizers and perform actions restricted to Full Admin so that ultimate control over the event stays with the accountable owner(s).
- [ ] Full Admin can elevate a Co-Organizer to Full Admin, or demote a Full Admin (other than themself, unless another Full Admin remains on the event) to Co-Organizer
- [ ] Only Full Admin can delete the event or remove another organizer from it
- [ ] Co-Organizers can perform all other event administrative actions identically to Full Admin — roster, divisions, brackets, weigh-in policy, disputes, devices, results (FR-2.8) — enforced server-side (NFR-2.5)
- [ ] Attempting a Full-Admin-only action as Co-Organizer is rejected with a clear authorization error and logged (NFR-2.11)

---

# EPIC 2 — Registration
*Coaches and registrants sign up athletes; the system assigns divisions.*
**Dependency ordering**: Requires E1. US-201/204 precede their dependent stories; US-210 depends on US-106.

### US-201 — Registrant account & profile (P3) [FR-1.1, FR-1.2]
As a registrant, I want an account with athlete profile(s) (name, DOB, rank, weight, academy) so that registration is fast and reusable.
- [ ] Profile fields validated (DOB plausible, weight within global bounds, academy free-text or picklist)
- [ ] Parent variant: multiple athlete profiles managed under one account (FR-1.4)
- [ ] Profile edits versioned; registrations snapshot profile data at registration time

### US-202 — Athlete self-registration (P3) [FR-2.2]
As an adult athlete, I want to register for an event and select my divisions so that I can compete.

**Given** an open registration window and a complete athlete profile
**When** I select the event and eligible divisions and submit
**Then** my registration is recorded with a fee total, and I receive a confirmation
**And** divisions whose criteria my profile doesn't match are not offered (FR-3.1)

### US-203 — Parent registers a minor (P3) [FR-1.4, FR-2.2]
As a parent, I want to register my child from my own account so that minors don't need accounts.

**Given** a parent account with a minor athlete profile
**When** I register the child for an event
**Then** the registration belongs to the child's profile but is managed (edit/withdraw) by my account

### US-204 — Coach academy roster (P2) [FR-1.3]
As a coach, I want to maintain my academy's athlete roster so that I can reuse it across events.
- [ ] Roster CRUD; athletes addable manually or by inviting an existing registrant profile
- [ ] Roster is coach-owned; object-level authorization enforced (NFR-2.5)

### US-205 — Coach bulk registration (P2) [FR-2.3]
As a coach, I want to register many athletes from my roster in one flow so that big-team signup takes minutes.

**Given** an open event and a roster with eligible athletes
**When** I select athletes, confirm/adjust each one's divisions, and submit as a batch
**Then** all selected athletes are registered atomically, with one combined fee summary

### US-206 — Bulk registration conflicts (P2) [FR-2.3]
As a coach, I want clear handling of duplicates and ineligible entries during bulk registration so that a partial problem doesn't force redoing the batch.

**Given** a batch where some athletes are already registered or ineligible for a chosen division
**When** I submit
**Then** valid entries succeed, problem entries are itemized with reasons, and I can fix and resubmit only those
**And** no athlete is double-registered for the same division (idempotent submission)

### US-207 — Pay-at-door election (P2, P3) [FR-2.4]
As a registrant or coach, I want to elect pay-at-the-door so that I can register without a card.

**Given** an event accepting pay-at-door
**When** I complete registration choosing pay-at-door
**Then** the registration is confirmed with an "owed" balance visible to me and the organizer (FR-2.5)

### US-208 — Card payment (stubbed) (P3) [FR-2.4]
As a registrant, I want to pay by card at registration so that I'm settled before event day.

**Given** an event with the card option configured
**When** I complete checkout via the payment provider abstraction
**Then** the mocked provider records a successful payment and my registration shows "paid"
**And** provider failure paths (decline, timeout) leave the registration unpaid-but-held with a clear retry path (D-06: no live Stripe in MVP)

### US-209 — Organizer roster management (P1) [FR-2.5]
As an organizer, I want to approve, withdraw, and correct registrations and track payment status so that my roster is accurate before brackets are built.
- [ ] Roster list filterable by division/academy/payment status (paid / owed / waived)
- [ ] Withdrawals and corrections recorded as events; corrections propagate to division assignment
- [ ] Organizer can mark owed balances as paid (cash at door) or waived

### US-210 — Automatic division assignment (P3-observed, system) [FR-3.1]
As a registrant, I want to land in the right division automatically so that I don't need to understand the organizer's division table.

**Given** divisions configured with weight/rank/age/gender ranges
**When** my registration is submitted or corrected
**Then** I'm assigned to the division(s) matching my profile, and mismatches surface to the organizer for manual override

### US-211 — Registrant edits registration (P3) [FR-1.2, FR-2.2]
As a registrant, I want to edit or withdraw my registration before the window closes so that mistakes are cheap to fix.

**Given** an open registration window
**When** I change division selection, update weight, or withdraw
**Then** the change re-runs division assignment and updates fees; after the window closes, edits require the organizer (US-209)

---

# EPIC 3 — Event Morning
*Hub goes live; devices pair; athletes check in and weigh in; brackets finalize.*
**Dependency ordering**: Requires E2 roster. US-301→US-302→US-303 gate all spoke stories. Bracket stories (US-311..314) depend on check-in/weigh-in outcomes.

### US-301 — Download event to hub (P1) [FR-4.1]
As an organizer, I want to download my full event (roster, divisions, brackets, schedule) to the Admin app so that event day runs with zero internet dependency.

**Given** a cloud event with a finalized roster
**When** I download the event to the Admin app while online
**Then** the hub holds the complete event state locally (SQLite) and a banner confirms "event-day ready — internet no longer required"
**And** a post-download roster change in the cloud warns me to re-sync before going offline

### US-302 — Hub LAN server start (P1) [FR-4.1, NFR-2.1]
As an organizer, I want to start hub mode so that judge and check-in devices can connect on the venue LAN.
- [ ] Embedded Kestrel starts with a hub-generated self-signed TLS cert (D-08)
- [ ] mDNS advertisement starts; hub screen shows IP and pairing QR (US-303)
- [ ] Hub health endpoint reports connected-device status (NFR-3.7)

### US-303 — Device pairing via QR (P4, P5) [FR-4.3, FR-4.4, NFR-2.1]
As a judge or check-in volunteer, I want to join the event by scanning one QR code so that setup takes seconds on event morning.

**Given** the hub displaying a pairing QR for role "Judge — Mat 2"
**When** I scan it with the Judge app
**Then** the app connects over WSS pinned to the hub's cert fingerprint, redeems the one-time token, and receives a device credential scoped to Mat 2
**And** re-using the same QR on a second device is rejected (token single-use)

### US-304 — Pairing fallback: manual IP (P4, P5) [FR-4.3]
As a judge on a venue network that blocks mDNS, I want to connect by typing the hub's IP so that discovery failure never blocks the event.

**Given** mDNS discovery finds no hub
**When** I enter the IP/port shown on the Admin screen and then complete the QR (or code) enrollment
**Then** pairing completes identically to US-303 (same cert pinning + token)

### US-305 — Device role management & revocation (P1) [FR-4.4]
As an organizer, I want to see paired devices, reassign roles, and revoke a device so that I control who can write what.

**Given** a paired device list on the hub
**When** I revoke a device or change its mat assignment
**Then** the change takes effect immediately; a revoked device's credential is rejected on next message (see US-508 for mid-event flow)

### US-306 — Check-in (P5) [FR-5.1]
As check-in staff, I want to mark an athlete present in a couple of taps so that the line keeps moving.

**Given** a paired Check-In device and the event roster
**When** I search/select an athlete and mark them present
**Then** the check-in is durably recorded as an event (append-only) and visible on the hub in real time (FR-4.7)

### US-307 — Weigh-in with range validation (P5) [FR-5.2, FR-5.3]
As weigh-in staff, I want instant in/out-of-range feedback when I record a weight so that exceptions are caught at the scale.

**Given** an athlete registered in a weight-bounded division
**When** I record their weight
**Then** in-range shows green confirmation; out-of-range flags the entry and routes it to the policy flow (US-308)
**And** the recorded weight is immutable history (corrections are new events)
**And** for an out-of-range entry, I can optionally attach a recommended resolution matching one of the event's configured policy options (DQ / auto-move / tolerance) — non-binding, surfaced to the organizer during resolution (D-25)

### US-308 — Missed-weight policy resolution (P5 initiates, P1 resolves) [FR-5.3]
As an organizer, I want out-of-range weigh-ins resolved per my configured policy so that exceptions are consistent and fast.

**Scenario: strict** — **Given** policy=strict, **When** weight is out of range, **Then** the athlete is marked DQ'd from that division pending my confirmation.
**Scenario: auto-move** — **Given** policy=auto-move, **When** a matching division exists and hasn't started, **Then** the app proposes the move and I confirm with one tap.
**Scenario: tolerance** — **Given** policy=tolerance X%, **When** the miss is within X%, **Then** the athlete passes with a tolerance annotation; beyond X% falls back to the strict flow.
**And** if Check-In/Weigh-in staff attached a recommended resolution (US-307) matching one of the configured policy options, it's displayed prominently on my resolution screen — it's a suggestion only and doesn't bind my decision (D-25)

### US-309 — Division move regenerates bracket (P1) [FR-5.3, FR-3.4]
As an organizer, I want an approved division move to regenerate the affected brackets automatically so that late changes don't require manual rebracketing.

**Given** an approved move into a division that has not started
**When** the move commits
**Then** both affected divisions' brackets regenerate preserving seeding rules (US-313), and the change is an auditable event
**And** moves into a started division are refused with an explanation

### US-310 — Check-in status board (P1, P5) [FR-5.4]
As an organizer, I want a live view of who's checked in, missing, and weighed per division so that I know when a division is ready to start.
- [ ] Per-division counts: registered / checked-in / weighed / cleared
- [ ] Missing-athlete list surfaced as division start approaches

### US-311 — Single-elimination bracket generation (P1) [FR-3.2, FR-3.5]
As an organizer, I want single-elimination brackets generated automatically with byes so that any division size works.

**Given** a division with N cleared athletes (N ≥ 2)
**When** I generate the bracket
**Then** a valid single-elim structure is produced with byes for non-power-of-two N, no athlete appears twice, and every athlete appears exactly once (PBT invariant, NFR-4.3)

### US-312 — Round-robin generation (P1) [FR-3.2]
As an organizer, I want round-robin for small divisions so that 3–4 athlete divisions get fair mat time.

**Given** a division at/below the round-robin size threshold (or manually selected)
**When** I generate the format
**Then** every athlete is scheduled against every other exactly once, with standings computed by wins then head-to-head/points tie-break

### US-313 — Seeding with academy separation (P1) [FR-3.3]
As an organizer, I want seeding to keep same-academy athletes apart in early rounds where possible so that finals aren't dojo-mate rematches.

**Given** a division with academy affiliations
**When** the bracket is generated
**Then** same-academy athletes are placed in different halves/quarters where mathematically possible, random otherwise, and I can manually adjust seeds before the division starts

### US-314 — Bracket regeneration before start (P1) [FR-3.4]
As an organizer, I want to regenerate a not-yet-started bracket after roster changes so that the printed/announced bracket matches reality.
- [ ] Regeneration allowed any number of times before first match starts; each is an event
- [ ] After start, structural edits are explicit organizer-only actions with confirmation (US-408)

---

# EPIC 4 — Competition
*Matches run; judges score; brackets advance; the organizer keeps control.*
**Dependency ordering**: Requires E3 (paired devices + generated brackets). US-401 precedes scoring stories.

### US-401 — Mat match queue (P4) [FR-6.1]
As a judge, I want to see my mat's matches in schedule order so that I always know who's up.

**Given** a paired Judge device assigned to Mat 2
**When** I open the queue
**Then** I see Mat 2's pending matches in order with athlete names/divisions, updating in real time as brackets advance (FR-4.7)
**And** I retain full read/write scoring authority only on Mat 2 (FR-4.5)

### US-402 — Point sparring scoring (P4) [FR-6.2]
As a judge, I want to enter point-sparring scores (points and penalties per competitor) so that the outcome is captured accurately.

**Given** an active match on my mat
**When** I record points/penalties and end the match
**Then** the winner is computed per point-sparring rules, I confirm, and the outcome event is durably stored locally before any acknowledgment (NFR-1.1)

### US-403 — Forms/kata scoring (P4) [FR-6.2]
As a judge, I want to enter per-judge numeric scores for forms competitors so that placements are computed fairly.

**Given** a forms division with configured judge count
**When** scores are entered for each competitor
**Then** the aggregate (avg with high/low drop when ≥5 judges) ranks competitors, ties broken per ruleset, and results are confirmable before commit

### US-404 — Outcome advances bracket (P4 observed, hub) [FR-6.3]
As a judge, I want a confirmed outcome to advance the bracket automatically so that the next match is ready without waiting on the head table.

**Given** a confirmed match outcome synced to the hub
**When** the hub applies it
**Then** the winner slots into the next round (or standings update in round-robin), and affected mat queues refresh in under 2 seconds on the LAN (NFR-5.2)

### US-405 — Dispute flag & resolution (P4 flags, P1 resolves) [FR-6.4]
As a judge, I want to flag a completed match for review so that disputes go to the organizer instead of stalling my mat.

**Given** a completed match
**When** I flag it with an optional note
**Then** the hub surfaces the dispute to the organizer; my mat continues with the next match
**And** the organizer's resolution (uphold/correct outcome) is recorded as an event; a corrected outcome re-runs bracket advancement safely

### US-406 — Mat authority enforcement (P4, system) [FR-4.5]
As an organizer, I want the hub to reject score writes for mats a device doesn't own so that the authority model is enforced, not advisory.

**Given** a Judge device credential scoped to Mat 2
**When** it submits an outcome for a Mat 3 match
**Then** the hub rejects the write with an authorization error and logs the attempt (NFR-2.11)
**And** read-only visibility into Mat 3's queue (US-410) never grants write authority — the rejection above is unchanged regardless of what the device can see

### US-407 — Real-time spoke updates (P4, P5) [FR-4.7]
As a judge, I want bracket and schedule changes pushed to my device so that my queue is never stale.

**Given** connected spoke devices
**When** the hub applies any event affecting them (advancement, schedule change, division move)
**Then** relevant spokes receive the update via SignalR within 2s (NFR-5.2), and a reconnecting spoke receives everything it missed (US-507)

### US-408 — Mid-event organizer edits (P1) [FR-3.4]
As an organizer, I want explicit, confirmed control to make structural changes after a division starts (injury withdrawal, mat reassignment, schedule shuffle) so that real-world chaos stays manageable.
- [ ] Post-start structural edits require typed confirmation and are recorded as attributed events
- [ ] Injury withdrawal advances the opponent; already-played results stand
- [ ] Mat reassignment moves remaining matches between mat queues atomically

### US-409 — Live standings on hub (P1) [FR-6.5]
As an organizer, I want live division standings and bracket progression on the Admin app so that I can answer "who's winning / what's next" instantly.
- [ ] Per-division bracket view with completed/pending matches and placements as they resolve
- [ ] Event-level progress: divisions complete / in progress / not started

### US-410 — Cross-mat visibility (P4) [FR-6.1]
As a judge, I want to glance at other mats' queues when I have a connection so that I have situational awareness of the whole event, not just my mat.

**Given** a paired Judge device assigned to Mat 2 with an active hub connection
**When** I switch to view another mat (e.g., Mat 3)
**Then** I see Mat 3's queue read-only — I cannot enter scores or outcomes for it (FR-4.5, enforced per US-406)
**And** if the connection drops, the view falls back to Mat 2 only until reconnected

### US-411 — Match focus/lock mode (P4) [FR-6.6]
As a judge, I want to lock my screen to the current in-progress match so that I don't mis-tap into another match or mat mid-scoring.

**Given** an active match on my assigned mat
**When** I enable focus mode
**Then** the screen restricts navigation to the current match's scoring flow (US-402/US-403) until I explicitly exit focus mode or the match ends
- [ ] Focus mode is optional and per-device; it does not change scoring logic or authority, only navigation

---

# EPIC 5 — Offline Resilience & Recovery (flagship)
*The differentiator: nothing is lost when networks fail.*
**Dependency ordering**: Behaviors span E3/E4 and must be designed with the sync core (first unit built), tested throughout.

### US-501 — Full event with zero internet (P1) [NFR-1.2, FR-4.1]
As an organizer, I want to run the entire event day — check-in through final results — with no internet at any point so that venue WiFi failure is a non-event.

**Given** an event downloaded to the hub (US-301) and the venue has no internet all day
**When** the event runs (pairing, check-in, weigh-in, scoring, advancement)
**Then** every function works identically to the connected case, and cloud replication simply begins whenever internet next appears (US-504)

### US-502 — Judge offline queue & replay (P4) [FR-4.6, NFR-1.1]
As a judge, I want my scores to queue on my device when the hub is unreachable and sync automatically when it's back so that I never re-enter a score.

**Given** the hub becomes unreachable (WiFi drop, hub reboot) mid-session
**When** I keep scoring matches on my queue
**Then** each outcome is durably persisted locally (SQLite) before the UI confirms it
**And** on reconnect, queued events replay in sequence order, idempotently — replaying twice changes nothing (PBT property, NFR-4.3)
**And** the UI shows queued-count and sync status honestly at all times

### US-503 — Check-in offline queue (P5) [FR-4.6, NFR-1.1]
As check-in staff, I want check-ins and weigh-ins to queue locally when disconnected so that the line never stops for network problems.

**Given** a disconnected Check-In device
**When** I continue processing athletes
**Then** entries queue durably and replay idempotently on reconnect, and out-of-range policy flows that need the organizer (US-308) are marked "pending hub"

### US-504 — Hub→cloud replication & outage replay (P1) [FR-4.6, NFR-1.1]
As an organizer, I want the hub to mirror its event log to the cloud whenever internet is available so that there's an off-site copy without me thinking about it.

**Given** intermittent venue internet
**When** connectivity is available
**Then** the hub replicates pending events to the cloud in sequence order with bounded retry/backoff (NFR-3.8)
**And** after an outage of any length, replay resumes from the last acknowledged sequence number with no gaps and no duplicates (cloud `AppendIfNotExists` idempotence)

### US-505 — Hub local backup export (P1) [FR-4.9]
As an organizer, I want automatic periodic and on-demand local backups of the event log so that I have a recovery path even with zero internet.
- [ ] Automatic snapshot at a configurable interval during a live event; manual "Export backup now"
- [ ] Backup is a portable, integrity-checked event-log snapshot (restorable via US-506)
- [ ] Backup files encrypted at rest consistent with D-09

### US-506 — Manual hub recovery (P1) [FR-4.8, NFR-3.12]
As an organizer whose Admin device just died, I want to stand up a replacement hub from the cloud replica or a local backup so that the event resumes with minimal loss.

**Given** a failed hub and a replacement device with the Admin app
**When** I restore from the cloud replica (if reachable) or the latest backup export
**Then** the new hub rebuilds full event state by replaying the event log, re-issues pairing QRs, and spokes re-pair and replay their queued events (US-502/503) to close any gap
**And** the documented runbook covers this end-to-end; hot standby remains out of scope (D-02)

### US-507 — Spoke auto-reconnect (P4, P5) [NFR-1.3]
As a judge or check-in volunteer, I want my device to reconnect and resync by itself so that I never fiddle with settings mid-event.

**Given** a paired spoke that lost connectivity
**When** the hub is reachable again
**Then** the device reconnects with its existing credential, uploads its queue, downloads missed updates, and shows "in sync" — with no user action

### US-508 — Mid-event device revocation (P1) [FR-4.4, NFR-2.1]
As an organizer, I want to revoke a lost or misbehaving device mid-event so that a missing tablet isn't a security hole.

**Given** a paired Judge device I can no longer trust
**When** I revoke it and issue a replacement pairing for that mat
**Then** the revoked credential is rejected on next contact; events it validly committed before revocation stand; the replacement device takes over the mat role

---

# EPIC 6 — Results & Wrap-Up
*Placements finalize; data lands in the cloud; registrants see history.*
**Dependency ordering**: Requires E4 completion per division; US-602 depends on US-504; US-603 depends on US-602.

### US-601 — Division finalization (P1) [FR-6.5]
As an organizer, I want to finalize a division's placements (1st/2nd/3rd) when its bracket completes so that awards can be announced immediately.
- [ ] Placements computed from the completed bracket/standings automatically; finalization is an explicit organizer event
- [ ] Reopening a finalized division (dispute resolution) requires typed confirmation and is audited

### US-602 — Post-event cloud completeness (P1) [FR-4.6]
As an organizer, I want confirmation that the cloud mirror holds 100% of the event log so that I can retire the hub device with confidence.

**Given** an ended event and eventual internet connectivity
**When** replication completes
**Then** the hub verifies cloud sequence completeness per device stream and shows "fully replicated — N events"; the success metric is zero data loss (NFR-1.1)

### US-603 — Registrant results & history (P2, P3) [FR-1.2]
As a registrant or coach, I want to see results and registration history in my account so that the event has a permanent record.
- [ ] Post-replication, division placements and match outcomes visible per athlete in the cloud app
- [ ] Coaches see all their roster's results for the event

---

# EPIC 7 — Reading Event Data
*Unit U9. Any signed-in user reads event data through a three-tier access model. Read-only — no story in this epic changes state.*
**Dependency ordering**: Requires E1 and E2 data to exist. US-701/702/703 are **authoritative** for authorization and must be implemented before the resource stories US-704..708, which describe response shape only (plan decision C2=C).

**Tiers**: **T0 Public** — any authenticated caller, event registration Open · **T1 Registrant** — caller has a non-withdrawn registration in the event · **T2 Organizer** — caller holds an organizer role on the event. Tiers are cumulative and evaluated per event; a caller matching no tier has no access.

## Tier stories (authoritative for authorization)

### US-701 — Public tier access to an open event (P2, P3) [U9-FR-2, U9-FR-3]
As any signed-in user, I want to see events that are open for registration so that I can find a tournament to enter.

**Given** an event whose registration status is Open
**When** any authenticated caller requests that event
**Then** they are granted tier T0 and receive the event **summary** — name, venue, date, registration window, entry fee, registration status

**Given** a T0 caller
**When** they request the event
**Then** detail-tier fields (card-enabled, check-in-started, created-by) are absent from the response

**Given** an event whose registration status is Draft or Closed, and a caller holding no higher tier
**When** they request that event
**Then** the response is 404 — the event is not discoverable

**Given** a T0 caller
**When** they request the registrant roster or the organizer roster for that event
**Then** the response is 404

**Given** an unauthenticated request
**When** it reaches any endpoint in this epic
**Then** the response is 401 — there is no anonymous access at any tier

### US-702 — Registrant tier access to an event I am in (P3, P2) [U9-FR-2, U9-FR-6]
As a registrant or coach, I want full detail of the events I have entered so that I can check what I signed up for.

**Given** a caller with a non-withdrawn registration in an event
**When** they request that event
**Then** they are granted tier T1 and receive the event **detail** — the summary plus card-enabled, check-in-started, weigh-in policy, and created-by

**Given** a T1 caller
**When** they request a registration record whose managing account is their own
**Then** the full registrant detail is returned, including date of birth, weight, rank, and gender

**Given** a T1 caller
**When** they request a registration record managed by a different account
**Then** the response is 404

**Given** a T1 caller
**When** they request the full registrant roster for the event
**Then** the response is 404 — T1 grants access to own records only, never to other registrants

**Given** a caller whose only registration in an event has been withdrawn
**When** they request that event
**Then** T1 is not granted; they fall back to T0 if registration is Open, and otherwise receive 404

### US-703 — Organizer tier access to an event I administer (P1) [U9-FR-2, U9-FR-7]
As an organizer, I want complete read access to every event I administer so that I can review my roster and co-organizers at any time.

**Given** a caller holding Full Admin or Co-Organizer on an event
**When** they request that event
**Then** they are granted tier T2 and receive the event detail **regardless of registration status** — including Draft and Closed events

**Given** a T2 caller
**When** they request the registrant roster
**Then** every non-withdrawn registration for the event is returned

**Given** a T2 caller
**When** they request the organizer roster
**Then** every account holding an organizer role on the event is returned with its role

**Given** a caller holding an organizer role on event A but not on event B
**When** they request any resource of event B
**Then** the response is 404 — organizer authority never spans events

**Given** two callers on the same event, one Full Admin and one Co-Organizer
**When** each performs the same read
**Then** both receive identical data — read access is not differentiated by organizer role

## Resource stories (response shape)

### US-704 — Read event summary and detail (P1, P2, P3) [U9-FR-1, U9-FR-3, U9-FR-9]
As a signed-in user, I want a single list of every event I can see so that I do not have to know an event id in advance.

**Given** a caller with any tier on one or more events
**When** they request the event collection
**Then** the union of their T0, T1, and T2 events is returned, each tagged with the caller's effective tier and, where held, their organizer role

**Given** the event collection
**When** it is returned
**Then** it is complete — no pagination, no page size limit, and no general-purpose filter or sort parameters are accepted

**Given** an event returned at summary level
**When** the response is inspected
**Then** it carries exactly: name, venue, date, registration start, registration end, entry fee, registration status

**Given** an event returned at detail level
**When** the response is inspected
**Then** it carries the summary fields plus card-enabled, check-in-started, weigh-in policy, and created-by account

### US-705 — Read divisions (P1, P2, P3) [U9-FR-1, U9-FR-10]
As a signed-in user, I want to see an event's divisions so that I can choose which ones to enter or review how the event is structured.

**Given** a caller with at least tier T0 on an event
**When** they request its divisions
**Then** each division is returned with its weight range, rank range, age range, gender, bracket format, and status

**Given** divisions whose status is Complete
**When** the collection is requested without an inclusion flag
**Then** they are excluded

**Given** the same request with `?includeCompleted=true`
**When** the collection is returned
**Then** completed divisions are included

**Given** a division id that exists but belongs to a different event
**When** it is requested under this event's path
**Then** the response is 404

### US-706 — Read the weigh-in policy (P1, P2, P3) [U9-FR-8]
As a prospective registrant, I want to know an event's missed-weight policy before I enter so that I can judge the risk of competing at my current weight.

**Given** a caller with at least tier T0 on an event
**When** they request the weigh-in policy
**Then** the policy mode (Strict, AutoMove, or Tolerance) is returned

**Given** a policy whose mode is Tolerance
**When** the policy is returned
**Then** the tolerance percentage is included

**Given** a policy whose mode is Strict or AutoMove
**When** the policy is returned
**Then** no tolerance percentage is present

**Given** an event
**When** the weigh-in policy is requested
**Then** exactly one policy is returned — there is no collection form of this resource

### US-707 — Read registrants (P1, P3) [U9-FR-1, U9-FR-5, U9-FR-10]
As an organizer, I want to review who has registered for my event so that I can check the roster before event day.

**Given** a T2 caller
**When** they request the registrant collection
**Then** each entry carries athlete name, academy, assigned division ids, payment status, and the assignment-mismatch flag

**Given** the registrant collection
**When** it is returned
**Then** date of birth, weight, rank, and gender are absent from every entry

**Given** a caller reading a single registration they are entitled to
**When** the record is returned
**Then** it adds date of birth, weight, rank, and gender to the collection fields

**Given** withdrawn registrations exist
**When** the collection is requested without an inclusion flag
**Then** they are excluded

**Given** the same request with `?includeWithdrawn=true`
**When** the collection is returned
**Then** withdrawn registrations are included and identifiable as withdrawn

### US-708 — Read organizer accounts and roles (P1) [U9-FR-1, U9-FR-7]
As a Full Admin, I want to see who administers my event and at what level so that I can verify the right people hold the right authority.

**Given** a T2 caller
**When** they request the account collection for the event
**Then** one entry is returned per account holding an organizer role, each carrying the account id, contact email, and role

**Given** any account entry
**When** it is returned
**Then** it carries no password data, MFA secret, recovery code, or session token

**Given** an account id that exists but holds no organizer role on this event
**When** it is requested under this event's path
**Then** the response is 404 — this endpoint never confirms the existence of unrelated accounts

## Cross-cutting stories

### US-709 — Non-disclosure and resource-id probing resistance (P1, P2, P3) [U9-FR-4, U9-FR-12]
As a user of the system, I want the API to refuse unauthorized reads without revealing what exists so that nobody can map the system's data by probing ids.

**Given** an event id that does not exist, and an event id that exists but on which the caller holds no tier
**When** each is requested
**Then** both return 404 with the same body — the two cases are indistinguishable to the caller

**Given** a resource id that is valid in its own right but belongs to a different event
**When** it is requested under an event the caller can read
**Then** the response is 404 — never 403, and never the resource

**Given** a caller systematically probing account, division, or registration ids
**When** they compare responses
**Then** status code and response body do not distinguish "exists but forbidden" from "does not exist"

**Given** any authorization failure on a read endpoint
**When** it occurs
**Then** it is logged with the acting account, event id, and endpoint, and the log entry contains no personal data

### US-710 — Conditional requests for cheap repeat reads (P1, P2, P3) [U9-FR-11]
As an API client, I want conditional requests so that polling an event I already hold does not re-transfer unchanged data.

**Given** any event-scoped read the caller is entitled to
**When** the response is returned
**Then** it carries a strong ETag derived from the event's log watermark

**Given** a request carrying `If-None-Match` equal to the current ETag
**When** it is handled
**Then** the response is 304 with no body, and no read-model tables are queried to produce it

**Given** any write that appends an event to that event's log
**When** the same read is repeated
**Then** the ETag differs from the previous value

**Given** the cross-event collection endpoint
**When** it is requested
**Then** no ETag is issued — its results span multiple event scopes and share no single watermark

**Given** an athlete updates their profile weight, and a client holds a prior ETag for a registrant detail record naming that athlete
**When** the client re-requests that record conditionally
**Then** it must not receive a 304 carrying the pre-update weight — this is the U9-CON-2 gap, and the design must close it either by excluding registrant detail from ETag coverage or by extending the watermark to cover the referenced athlete scopes

---

# Traceability Matrix (FR → Stories)

| Requirement | Stories |
|---|---|
| FR-1.1 | US-101, US-102, US-201 |
| FR-1.2 | US-201, US-211, US-603 |
| FR-1.3 | US-204 |
| FR-1.4 | US-201, US-203 |
| FR-1.5 | US-103 |
| FR-1.6 | (cross-cutting RBAC role model — enforced via US-108/109 criteria) |
| FR-2.1 | US-104, US-105 |
| FR-2.2 | US-202, US-203, US-211 |
| FR-2.3 | US-205, US-206 |
| FR-2.4 | US-107, US-207, US-208 |
| FR-2.5 | US-207, US-209 |
| FR-2.6 | (policy decision D-05 — no story needed; absence of tier logic) |
| FR-2.7 | US-108 |
| FR-2.8 | US-109 |
| FR-3.1 | US-106, US-210 |
| FR-3.2 | US-106, US-311, US-312 |
| FR-3.3 | US-313 |
| FR-3.4 | US-309, US-314, US-408 |
| FR-3.5 | US-311 |
| FR-4.1 | US-301, US-302, US-501 |
| FR-4.2 | (cross-cutting event-sourcing mechanism — verified via US-306/402/502/504 criteria) |
| FR-4.3 | US-303, US-304 |
| FR-4.4 | US-303, US-305, US-508 |
| FR-4.5 | US-406 |
| FR-4.6 | US-502, US-503, US-504, US-602 |
| FR-4.7 | US-306, US-401, US-407 |
| FR-4.8 | US-506 |
| FR-4.9 | US-505 |
| FR-5.1 | US-306 |
| FR-5.2 | US-307 |
| FR-5.3 | US-105, US-307, US-308, US-309 |
| FR-5.4 | US-310 |
| FR-6.1 | US-401, US-410 |
| FR-6.2 | US-402, US-403 |
| FR-6.3 | US-404 |
| FR-6.4 | US-405 |
| FR-6.5 | US-409, US-601 |
| FR-6.6 | US-411 |

## Unit U9 — Read/Query API (U9-FR → Stories)

| Requirement | Stories |
|---|---|
| U9-FR-1 Nine GET endpoints | US-704, US-705, US-706, US-707, US-708 |
| U9-FR-2 Three-tier access model | US-701, US-702, US-703 |
| U9-FR-3 Event collection spans tiers | US-701, US-704 |
| U9-FR-4 Tier verified before resource returned | US-709 |
| U9-FR-5 Minimal list / full detail | US-707 |
| U9-FR-6 T1 own-registration only | US-702, US-707 |
| U9-FR-7 Organizer roster only | US-703, US-708 |
| U9-FR-8 Weigh-in policy read-only, single | US-706 |
| U9-FR-9 No pagination or general filtering | US-704 |
| U9-FR-10 Inclusion flags for excluded rows | US-705, US-707 |
| U9-FR-11 ETag / conditional requests | US-710 |
| U9-FR-12 Denial indistinguishable from absence | US-709 |

All twelve U9 requirements map to at least one story. **INVEST note for Epic 7**: US-704..708 are
independent of one another but depend on US-701..703, which own the authorization criteria — a
deliberate consequence of the plan decision C2=C, not an oversight.

Every FR maps to ≥1 story (or a documented rationale); every story maps to ≥1 FR. INVEST reviewed per story: stories are independently deliverable within epic dependency order, negotiable in detail, valuable to a named persona, estimable at medium granularity, small (single capability/scenario), and testable via their criteria.

# Ordering Summary (delivery dependencies)

```text
E1 Pre-Event Setup
  -> E2 Registration
       -> E3 Event Morning  (needs roster; needs sync core for hub/pairing)
            -> E4 Competition (needs paired devices + brackets)
E5 Offline Resilience: designed first (sync core), verified across E3/E4
E6 Results & Wrap-Up: after E4 per division; cloud completeness after E5 replication
E7 Reading Event Data: after E1/E2 data exists; US-701..703 (tiers) precede US-704..710
```

The sync/event-log core underpinning E5 is the deepest dependency in the system and should be the first unit built — this will drive Units Generation.
