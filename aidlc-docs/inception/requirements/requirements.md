# EventManager — MVP Requirements

**Source inputs**: `aidlc-inputs/vision.md`, `aidlc-inputs/tech-env.md`, `requirement-verification-questions.md` (15 answers), `requirement-clarification-questions.md` (8 answers)
**Depth**: Comprehensive
**Date**: 2026-07-22

---

## 1. Intent Analysis

| Aspect | Assessment |
|---|---|
| User request | Build EventManager — offline-first tournament management software for independent dojo owners (50–300 athletes) — per vision.md and tech-env.md |
| Request type | New Project (greenfield) |
| Request clarity | Clear, with ambiguities resolved via two question rounds |
| Scope estimate | Cross-system: 3 client apps (Admin hub, Judge, Check-In), cloud backend, shared sync/event-log library |
| Complexity estimate | Complex — distributed offline-first event-sourcing system with LAN sync |

## 2. Decision Log (from clarification rounds)

| # | Decision | Answer |
|---|---|---|
| D-01 | Spectator app | **Excluded from MVP** (Phase 2). MVP builds Admin, Judge, Check-In only |
| D-02 | Hub failover | **Manual recovery only** in MVP (restore from cloud replica or device backup); hot standby fully deferred. *Noted deviation from vision.md's "hot-standby design from day one" risk note — accepted by user* |
| D-03 | Scoring model | **Point sparring + forms/kata**; architecture must be extensible to additional rulesets (e.g., grappling) later |
| D-04 | Bracket formats | **Single elimination + round robin**; design open for future formats (e.g., double elimination) |
| D-05 | Tiers/billing | **No tier logic in MVP** — everything free; tiers/billing later |
| D-06 | Payments | **Optional per event**: "pay at the door" tracking always available; **Stripe integration stubbed/mocked** behind an abstraction — no live Stripe account for MVP |
| D-07 | Code structure | **Simulated multi-repo**: top-level folder per app, each with its own solution; shared library consumed via local NuGet feed |
| D-08 | LAN security | **Pinned self-signed TLS + one-time pairing token with device roles** (QR pairing; fully SECURITY-01/08 compliant) |
| D-09 | Local DB encryption | **SQLCipher (or equivalent EF Core-compatible) on all clients** |
| D-10 | Cloud deployment | **Provider-agnostic**: Dockerfiles + Docker Compose only; no provider-specific IaC in MVP |
| D-11 | Weigh-in miss policy | **Configurable per event**: strict disqualification, auto-move, or tolerance percentage |
| D-12 | Accounts | **Accounts for organizers, coaches, and athletes** (self-service edits, history) |
| D-13 | Extensions | Security Baseline: **ON (full)**; Property-Based Testing: **ON (full)**; Resiliency Baseline: **ON** |
| D-14 | DR strategy | **Single-region, multi-zone; no cross-region DR in MVP**; consider Warm Standby post-MVP |
| D-15 | Change management | **Propose lightweight process** (change record + approval + rollback note) |
| D-16 | CI/CD | **Propose pipeline** — GitHub Actions: build, test, coverage gate, Docker image publish |
| D-17 | Rollback | **Version-pinned rollback** (redeploy previous image/artifact) |
| D-18 | Deployment style | **Direct/in-place** — acceptable because the hub tolerates cloud downtime by design |
| D-19 | Incident response | **Propose lightweight IR + COE process** |
| D-20 | Multi-organizer permission model | **RBAC with two default roles per event**: **Full Admin** and **Co-Organizer**. Co-Organizer has the same day-to-day event admin rights the single Organizer had (brackets/divisions/schedule/disputes/devices/results); Full Admin additionally can delete the event, remove other organizers, and grant/transfer Full Admin. Architecture generalized around RBAC (not hardcoded to two roles); default role granted depends on how the organizer joined the event (creator vs. added) |
| D-21 | Adding organizers to an event | **Both** mechanisms supported: email invite (invitee must have or create an Organizer account to accept) and direct add of an existing known Organizer account by lookup |
| D-22 | Organizer count cap | **No cap** — any number of organizers per event in MVP |
| D-23 | NFR-5.1 scale envelope revision | Removed "single organizer per event"; replaced with a typical-count sizing assumption (no hard cap enforced, D-22) |
| D-24 | Judge cross-mat visibility & focus mode | Judge app grants **read/write authority only on the assigned mat** (unchanged, FR-4.5); when the device has an active hub connection it may additionally show other mats' queues **read-only** for situational awareness; offline/disconnected, only the assigned mat is shown. App also supports a **focus/lock mode** restricting the screen to the current in-progress match to reduce mis-taps |
| D-25 | Check-In recommendation on out-of-range weigh-ins | Check-In/Weigh-in staff may attach a **non-binding recommended resolution** (matching the event's configured policy options — DQ / auto-move / tolerance) to an out-of-range weigh-in event; surfaced prominently to the organizer during resolution (US-308); does **not** change decision authority — resolution remains an organizer action (Full Admin or Co-Organizer, FR-2.8) |
| D-26 | Identifier strategy (Snowflake) | **Snowflake IDs** (64-bit, time-sortable) for all identifiers created on one node and referenced across apps or in the shared event log; local-only read-model surrogate keys unchanged. `TournamentEvent` carries `EventId` (Snowflake — PK, idempotence key, sort, cross-app ref) **and** `DeviceId` + per-device contiguous `SequenceNumber` (retained for gap-free replication). Generator lives in `EventManager.Sync`; worker IDs assigned at pairing/download (hub authority; cloud reserved range; event-scoped uniqueness). Cross-device time ordering is best-effort; authoritative per-stream order is the sequence number. *(Application Design Q8/Q9/Q10)* |
| D-27 | Event-day organizer topology & hub RBAC | MVP runs event day from a **single Admin/hub device** (no admin-client capability). The **hub enforces organizer RBAC offline** (Full Admin vs Co-Organizer) using role assignments packaged at event download — server-side checks on the hub, Security-Baseline (SECURITY-08) compliant. Offline authentication mechanism deferred to Functional Design. *(Application Design Q5a/Q5b)* |

## 3. Personas (summary)

- **Organizer (dojo owner)** — creates/configures events, divisions, brackets; runs the Admin hub on event day. An event can have multiple Organizers via RBAC: **Full Admin** and **Co-Organizer** (D-20); the creator is Full Admin by default
- **Coach** — bulk-registers athletes from their academy; manages roster; has an account
- **Athlete (or parent on behalf of a minor)** — registers online, pays (or elects pay-at-door); has an account
- **Judge/Scorekeeper** — scores matches on an assigned mat via the Judge app (read/write on assigned mat; read-only visibility into other mats when connected, D-24); no cloud account needed (device pairing grants role)
- **Check-in/Weigh-in staff** — marks athletes present, records weights via the Check-In app; device-paired role; may attach non-binding policy recommendations on out-of-range weigh-ins (D-25)

## 4. Functional Requirements

### FR-1 Accounts & Authentication (cloud)
- **FR-1.1** Organizer, coach, and athlete account registration/login via ASP.NET Core Identity issuing JWTs (self-hosted; no third-party IdP)
- **FR-1.2** Athlete accounts support self-service profile edits (name, DOB, rank, weight, academy) and registration history
- **FR-1.3** Coach accounts manage a persistent academy roster reusable across events
- **FR-1.4** Parents may manage minor athletes' registrations under their own account
- **FR-1.5** MFA (e.g., TOTP) supported for organizer (administrative) accounts *(required by SECURITY-12)*
- **FR-1.6** Organizer role model (RBAC, D-20): each event has one or more organizer role-assignments — **Full Admin** or **Co-Organizer**; role checks enforced server-side on every organizer action (NFR-2.5); model generalized for future roles beyond the two MVP defaults

### FR-2 Event & Registration Management (cloud, pre-event)
- **FR-2.1** Organizer creates an event: name, date, venue, registration window, entry fees, weigh-in policy configuration (D-11); the creating organizer is assigned **Full Admin** on the new event by default (D-20)
- **FR-2.2** Athletes register for an event online: select divisions, provide weight/rank/age/gender
- **FR-2.3** Coach bulk registration: register multiple athletes from academy roster in one flow
- **FR-2.4** Payment at registration: "pay at the door" always available (tracked as owed/paid); card payment path present but implemented against a **stubbed/mocked payment provider abstraction** (D-06) — no live Stripe in MVP
- **FR-2.5** Organizer views/edits the roster: approve, withdraw, correct registrations; payment status tracking (paid / owed / waived)
- **FR-2.6** Everything is free in MVP — no tier limits or billing enforcement (D-05)
- **FR-2.7** Organizer management: a Full Admin can add additional organizers to the event by email invite (invitee must have or create an Organizer account to accept, D-21) or by directly adding an existing Organizer account by lookup (D-21); no cap on the number of organizers per event (D-22); added organizers default to Co-Organizer, elevatable to Full Admin by an existing Full Admin
- **FR-2.8** Full-Admin-only event actions: delete the event, remove/demote another organizer, grant/transfer Full Admin role. All other event administrative actions — roster management (FR-2.5), divisions/brackets (FR-3.x), weigh-in policy (FR-5.x), dispute resolution (FR-6.4), device management (FR-4.4), results finalization (FR-6.5) — are available to both Full Admin and Co-Organizer

### FR-3 Divisions, Brackets & Seeding
- **FR-3.1** Division configuration by weight/rank/age/gender ranges; automatic assignment of registered athletes to matching divisions; manual override
- **FR-3.2** Automatic bracket generation per division: **single elimination** and **round robin** (D-04); format selectable per division with sensible defaults by division size; bracket-format engine extensible to future formats
- **FR-3.3** Seeding: random baseline with manual adjustment; **academy separation** — athletes from the same academy placed to avoid meeting in early rounds where possible
- **FR-3.4** Bracket regeneration allowed until the division is started; post-start structural edits are organizer-only explicit actions (recorded as events)
- **FR-3.5** Byes handled automatically for non-power-of-two brackets

### FR-4 Offline-First Sync & Admin Hub
- **FR-4.1** Admin app embeds a Kestrel server and acts as the LAN hub; the full event (roster, divisions, brackets, schedule) is downloaded to the hub before event day and operable with **zero internet dependency**
- **FR-4.2** Every state change (registration edit, check-in, weigh-in, score, bracket advancement, schedule update) is an immutable, timestamped, sequence-numbered event; current state is a projection of the event log. Each event carries a **Snowflake `EventId`** (time-sortable, minted at origin) as PK/idempotence key plus `DeviceId` + per-device contiguous `SequenceNumber` (D-26)
- **FR-4.3** Judge/Check-In apps discover the hub via mDNS with documented fallbacks: **manual IP entry and QR pairing** (QR is also the security-pairing mechanism, D-08)
- **FR-4.4** Device pairing: one QR scan conveys hub address + cert fingerprint + one-time enrollment token; hub assigns a device credential and role (e.g., "Judge — Mat 2", "Check-In"); tokens are single-use; organizer can revoke a device
- **FR-4.5** Authority model enforced by the hub: hub authoritative for bracket structure/divisions/schedule; each Judge device authoritative (read/write) only for its assigned mat — read-only visibility into other mats is permitted while connected (FR-6.1, D-24) but never grants write authority; Check-In append-only, optionally annotated with non-binding recommendations (FR-5.3, D-25)
- **FR-4.6** Spoke apps queue events locally (SQLite) when disconnected from the hub and replay idempotently on reconnect; hub replicates its event log to the cloud asynchronously whenever internet is available, in sequence order; cloud is a mirror, never a conflicting source of truth
- **FR-4.7** Real-time push from hub to spokes via SignalR (bracket updates, schedule changes, results)
- **FR-4.8** Manual hub recovery (D-02): a replacement Admin device can restore full event state from the cloud replica (if reachable) or from a local backup export; recovery procedure documented as a runbook. Hot standby is out of scope for MVP
- **FR-4.9** Hub can export a local backup (event log snapshot) on demand and automatically at intervals during the event

### FR-5 Check-In & Weigh-In
- **FR-5.1** Staff mark athletes present; check-in status visible on hub and relevant spokes in real time
- **FR-5.2** Weigh-in recording with automatic validation against the athlete's registered weight class
- **FR-5.3** Out-of-range handling per event configuration (D-11): strict disqualification, auto-move to matching division, or allowance tolerance percentage; all outcomes recorded as events; division moves trigger bracket regeneration only if the division hasn't started; Check-In/Weigh-in staff may attach a non-binding recommended resolution matching the configured policy options, surfaced to the organizer during resolution (D-25)
- **FR-5.4** Queue/status view: who's checked in, who's missing, per division

### FR-6 Judge Scoring & Live Results
- **FR-6.1** Judge app operates per assigned mat: shows that mat's match queue in schedule order (full read/write scoring authority, FR-4.5); when the device has an active hub connection, it additionally shows other mats' queues read-only for situational awareness; offline/disconnected, only the assigned mat's queue is shown (D-24)
- **FR-6.2** Scoring models for MVP (D-03): **point sparring** (points per competitor, penalties, timer-independent entry) and **forms/kata** (per-judge numeric scores, aggregate/average, tie-break); scoring engine designed for pluggable additional rulesets
- **FR-6.3** Match outcome recording: winner, score detail, win method; outcome advances the bracket automatically on the hub
- **FR-6.4** Dispute flagging: a judge can flag a completed match for organizer review; flag visible on the hub; organizer resolution recorded as an event
- **FR-6.5** Live results: hub maintains real-time division standings and bracket progression, visible on the Admin app (spectator delivery is Phase 2)
- **FR-6.6** Judge app supports a focus/lock mode restricting the screen to the current in-progress match, to reduce mis-taps and accidental navigation during scoring; existing scoring flows (FR-6.2/FR-6.3) unaffected (D-24)

## 5. Non-Functional Requirements

### NFR-1 Offline-First Reliability (flagship)
- **NFR-1.1** Zero data loss across connectivity failures: 100% of accepted events (scores, check-ins, weigh-ins) durably persisted locally before acknowledgment; idempotent replay end-to-end (spoke→hub→cloud)
- **NFR-1.2** Full event-day operation with no internet, indefinitely; cloud replication resumes automatically on reconnect
- **NFR-1.3** Hub tolerates spoke disconnect/reconnect without operator intervention; spokes resync automatically

### NFR-2 Security (Security Baseline — full enforcement)
- **NFR-2.1** LAN transport: WSS/HTTPS with hub-generated self-signed cert, fingerprint-pinned at pairing; device enrollment via one-time tokens; role-scoped device credentials on every hub connection (D-08) — SECURITY-01, -08
- **NFR-2.2** Cloud transport: TLS 1.2+ on all API traffic — SECURITY-01
- **NFR-2.3** At rest: SQLCipher (or equivalent) encryption on all client SQLite databases (D-09); PostgreSQL storage encryption enabled — SECURITY-01
- **NFR-2.4** Input validation (FluentValidation/Data Annotations) on all API and hub request models before the event-log write path; parameterized queries only (EF Core) — SECURITY-05
- **NFR-2.5** AuthN/AuthZ: deny-by-default on all cloud endpoints; object-level ownership checks generalized to per-event RBAC role-assignments (organizer↔event: Full Admin / Co-Organizer, FR-1.6; coach owns roster; athlete owns profile); Full-Admin-only actions enforced server-side (FR-2.8); server-side role checks; JWT validated on every request. The **Admin hub enforces the same organizer RBAC offline** on event-day admin actions using role assignments downloaded with the event (D-27) — SECURITY-08
- **NFR-2.6** Credential management: adaptive password hashing (Identity default), breached-password check, brute-force lockout, MFA for organizer accounts, secrets via environment/secret manager — never in source — SECURITY-12
- **NFR-2.7** Structured logging (timestamp, correlation ID, level) with no PII/secrets in logs; centralized log routing for the cloud backend — SECURITY-03
- **NFR-2.8** HTTP security headers on any HTML-serving endpoint; hardening baseline (no default creds, generic production errors, no stack traces) — SECURITY-04, -09
- **NFR-2.9** Supply chain: locked dependency versions, vulnerability scanning in CI, pinned Docker base images, SBOM for the backend image — SECURITY-10
- **NFR-2.10** Rate limiting on public cloud endpoints (registration, login) — SECURITY-11
- **NFR-2.11** Event log is append-only and auditable (who/what/when per event) — SECURITY-13, -14
- **NFR-2.12** Fail-safe error handling: global exception handlers in backend and hub server; fail closed; resource cleanup on error paths — SECURITY-15

### NFR-3 Resiliency (Resiliency Baseline — directional)
- **NFR-3.1** Workload criticality (RESILIENCY-01): Admin hub + event-log path = **Critical** (event-day operation); cloud backend = **Medium** (pre-event registration and mirror; outage does not stop a running event)
- **NFR-3.2** Targets (RESILIENCY-02, D-14): cloud backend availability target **99.5%**; **RTO ≤ 4 hours** (redeploy via Compose + restore); **RPO ≤ 24 hours for cloud-originated data** (registrations/accounts) via automated daily PostgreSQL backups — event-day data has effective **RPO ≈ 0** because the hub re-replays its event log after any cloud outage. Single-region, multi-zone-capable; Warm Standby evaluated post-MVP
- **NFR-3.3** Change management (D-15): lightweight proposed process — PR review + change record + rollback note per production deploy
- **NFR-3.4** CI/CD (D-16): GitHub Actions — build, xUnit tests (incl. PBT with seed logging), 80% coverage gate on sync/event-log core, Docker image build with pinned tags
- **NFR-3.5** Rollback (D-17): version-pinned image redeploy; EF Core migrations must be backward-compatible one version (expand/contract) so image rollback is safe
- **NFR-3.6** Deployment style (D-18): direct/in-place; brief backend downtime acceptable by design
- **NFR-3.7** Health checks (RESILIENCY-06): shallow `/health` + deep DB-connectivity check on the cloud backend; hub exposes LAN health endpoint for spoke connection status
- **NFR-3.8** Timeouts on all external calls; hub↔cloud replication uses bounded retry with backoff; graceful degradation is the architecture's core premise (RESILIENCY-10)
- **NFR-3.9** Monitoring (RESILIENCY-05/07): structured logs + key metrics (replication lag, queued-event depth, error rates) with alerting; dashboard definition for backend health
- **NFR-3.10** Backups (RESILIENCY-12): automated daily PostgreSQL backups, retention ≥ 30 days, encrypted; hub local backup exports (FR-4.9); documented restore validation procedure
- **NFR-3.11** Incident response (D-19): lightweight proposed IR + COE process (alert → triage runbook → post-incident COE with corrective actions)
- **NFR-3.12** Failover/recovery runbooks (RESILIENCY-13): documented manual hub-recovery runbook (FR-4.8) and cloud restore runbook

### NFR-4 Testing & Quality (PBT — full enforcement)
- **NFR-4.1** xUnit across backend and client logic; **80%+ coverage on core sync/event-log logic** (event-sourcing engine, idempotent replay, conflict handling); lighter elsewhere
- **NFR-4.2** PBT framework: **FsCheck (xUnit integration)** (PBT-09); domain generators for events, brackets, divisions, weights (PBT-07); shrinking enabled and seeds logged in CI (PBT-08)
- **NFR-4.3** Mandatory property coverage (PBT-01…06, -10): event serialization round-trips; replay idempotence (`apply(apply(log)) = apply(log)`); bracket invariants (participant preservation, exactly-one-winner, valid advancement); seeding invariants (academy separation when feasible); projection oracle tests (optimized projection vs. naive fold); stateful model tests for the event-log store and bracket engine; example-based tests pin all business-critical scenarios alongside PBT
- **NFR-4.4** CI gates: build + unit tests + coverage threshold block merge; integration tests for LAN disconnect/reconnect-replay recommended post-MVP (not a required gate yet)

### NFR-5 Performance & Scale
- **NFR-5.1** Scale envelope: 300 athletes, ~8 mats, ~20 concurrent LAN devices, typically 1-3 organizers per event (Full Admin + Co-Organizers), no hard cap (D-22, D-23)
- **NFR-5.2** LAN interactions (score entry ack, check-in ack) < 500 ms on-hub; hub→spoke push propagation < 2 s
- **NFR-5.3** Hub cold start with a full 300-athlete event (log replay to projections) < 30 s on typical organizer hardware
- **NFR-5.4** Cloud backend sized for pre-event registration bursts (hundreds of concurrent users), not event-day load

### NFR-6 Platform & Structure
- **NFR-6.1** C# 13 / .NET 10 LTS everywhere; .NET MAUI for all client apps (Admin: Windows/Mac/iPad; Judge/Check-In: iOS/Android); ASP.NET Core Web API backend; EF Core (Npgsql cloud / SQLite local); no JS/TS/native codebases (per tech-env.md prohibitions)
- **NFR-6.2** Repository layout (D-07): simulated multi-repo — top-level folder per app (`admin/`, `judge/`, `checkin/`, `backend/`, `shared/`), each with its own solution; shared sync/event-log library versioned and consumed via a local NuGet feed
- **NFR-6.3** Local schema migrations never run automatically during an active tournament — on app upgrade only, with rollback path
- **NFR-6.5** Identifier strategy (D-26): Snowflake IDs for all cross-app/log identifiers; single `IIdGenerator` in `EventManager.Sync`; stored as `BIGINT` (PostgreSQL) / `INTEGER` (SQLite); worker-ID uniqueness guaranteed by pairing/download-time allocation
- **NFR-6.4** Deployment (D-10): backend as Docker image + Docker Compose (API + PostgreSQL); provider-agnostic; no cloud-provider IaC in MVP

## 6. Out of Scope (MVP)

| Item | Disposition |
|---|---|
| Spectator mobile experience | Phase 2 |
| Hub hot standby / automatic failover | Post-MVP (manual recovery runbook only, D-02) |
| Live Stripe payments, coupons, early-bird pricing, in-app refunds | Post-MVP (provider abstraction stubbed, D-06) |
| Free/paid tier limits & billing | Post-MVP (D-05) |
| Federation/multi-event management | v2+ |
| Additional scoring rulesets (grappling/BJJ, continuous sparring) | Post-MVP (pluggable engine required now) |
| Double elimination and other bracket formats | Post-MVP (extensible format engine required now) |
| Gym-management integrations (Mindbody etc.) | Open question, out of scope |
| Custom/configurable organizer roles beyond Full Admin/Co-Organizer defaults | Post-MVP (RBAC architecture supports it, D-20) |
| Cross-region DR / Warm Standby | Evaluate post-MVP (D-14) |

## 7. Key Risks Carried Forward

1. **Hub device failure mid-event** — mitigated only by cloud replica + local backup exports + documented manual recovery in MVP (user-accepted deviation from vision.md's day-one hot-standby note; the event-log architecture is the enabler for adding hot standby later)
2. **mDNS blocked on venue networks** — mitigated: QR pairing and manual IP entry are first-class (FR-4.3), not afterthoughts
3. **MAUI TLS pinning + SQLCipher integration complexity** across Windows/Mac/iOS/Android — flagged for early spike during Construction
4. **Solo-dev support capacity** — process answers (D-15/16/19) deliberately lightweight

## 8. Extension Compliance Summary (Requirements stage)

| Rule set | Status |
|---|---|
| SECURITY-01…15 | Addressed as requirements (NFR-2.x); design/code verification occurs at later stage gates |
| PBT-01…10 | Framework selected (FsCheck), property categories mandated (NFR-4.2/4.3); property identification per unit occurs in Functional Design |
| RESILIENCY-01…15 | User decisions captured (D-14…D-19); targets documented (NFR-3.2); RESILIENCY-14 (testing approach) question deferred to NFR Design per rule; runbook/monitoring obligations mapped (NFR-3.x) |
