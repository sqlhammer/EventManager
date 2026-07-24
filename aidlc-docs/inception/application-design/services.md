# Application Design — Services & Orchestration

**Stage**: INCEPTION - Application Design
**Date**: 2026-07-24
**Altitude**: Service definitions, responsibilities, and orchestration patterns (how components collaborate across a flow). Detailed logic deferred to Functional Design.

A "service" here is an orchestration boundary — it coordinates domain engines, the event store, and transport. Services below are grouped by the flow they own.

---

## S-1 — Registration Service (cloud)
**Owns**: online registration for athletes, coaches (bulk), parents (FR-2.2/2.3, US-201–208).
**Orchestration**:
1. Validate request via `Contracts` validators (NFR-2.4).
2. `DivisionAssignmentService` matches profile → division(s) (FR-3.1, US-210).
3. Persist as events via `PostgresEventStore.AppendIfNotExists`; Snowflake IDs minted in cloud worker range (Q10).
4. `CloudProjectionHost` updates roster/registration read models.
5. Payment path calls the **stubbed payment provider abstraction** (D-06) — never live Stripe.

## S-2 — Organizer & RBAC Service (cloud + hub)
**Owns**: organizer accounts, add/manage co-organizers, role decisions (FR-1.6/2.7/2.8, US-108/109).
**Orchestration**:
- Cloud: `OrganizerController` → `OrganizerRoleService` → `RoleAuthorizationPolicy` (Domain) for every organizer action; deny-by-default (NFR-2.5). Full-Admin-only actions gated (delete event, remove/demote, transfer).
- Hub: `OrganizerAuthService` enforces the **same** `RoleAuthorizationPolicy` offline using role assignments packaged at event download (Q5b=A). Single Admin device on event day (Q5a=A).
- Role assignments are events; they replicate and download like any other state.

## S-3 — Event Download & Readiness Service (hub)
**Owns**: making the hub "event-day ready" with zero internet dependency (FR-4.1, US-301).
**Orchestration**:
1. `EventDownloadService` pulls the full event stream + role assignments + worker-ID reservations while online.
2. Writes to SQLCipher-encrypted `HubEventStore`; `HubProjectionHost.RebuildAsync` folds the log (Q3).
3. Readiness gate raised; post-download cloud changes trigger a re-sync warning.

## S-4 — Pairing & Device Service (hub ↔ spokes)
**Owns**: device enrollment, roles, worker-ID assignment, revocation (FR-4.3/4.4, US-303/304/305/508).
**Orchestration**:
1. `PairingService` issues QR = hub address + cert fingerprint + one-time token + role.
2. Spoke `PairingClient` discovers hub (mDNS → manual-IP/QR fallback), pins cert, redeems token.
3. Hub assigns `DeviceCredential` + **Snowflake worker ID** via `WorkerIdRegistry` (Q10); token is single-use.
4. Revocation frees the worker ID and rejects the credential on next contact.

## S-5 — Check-In / Weigh-In Service (spoke → hub)
**Owns**: presence + weight capture with policy handling (FR-5.x, US-306–310).
**Orchestration**:
1. `checkin-app` view-models capture; `WeighInPolicyEvaluator` gives instant range feedback (US-307).
2. Out-of-range attaches an optional **non-binding recommendation** (D-25) and routes to the organizer.
3. Events durably queued via `ClientSync.LocalEventQueue`, replayed idempotently to the hub.
4. Hub `WeighInPolicyService` applies the organizer's resolution; division move → `BracketService` regeneration if not started (US-309).

## S-6 — Scoring & Advancement Service (spoke → hub)
**Owns**: mat scoring → bracket advancement → live results (FR-6.x, US-401–409).
**Orchestration**:
1. `judge-app` `ScoringViewModel` builds inputs; `ScoringEngine` (Domain) computes outcome; **durable-before-ack** (NFR-1.1).
2. Event replays to hub; `HubServer` validates **mat authority** (rejects foreign-mat writes, US-406) — read-only cross-mat view never grants write (US-410).
3. `BracketService.ApplyOutcome` advances the bracket; `SignalRPushService` pushes updates to spokes within 2s (NFR-5.2).
4. Disputes via `DisputeService`; organizer resolution recorded as an event (US-405).

## S-7 — Replication Service (hub → cloud)
**Owns**: asynchronous, sequence-ordered cloud mirroring (FR-4.6, US-504/602).
**Orchestration**:
1. `ReplicationClient` uses `ReplicationProtocol` to compute the next batch from cloud high-water marks.
2. Sends to `EventIngestController.IngestBatch`; cloud `AppendIfNotExists` guarantees no duplicates.
3. Bounded retry/backoff (NFR-3.8); resumes from last acked sequence with no gaps after any outage.
4. Post-event completeness verification (US-602): "fully replicated — N events."

## S-8 — Backup & Recovery Service (hub)
**Owns**: local backups and manual hub recovery (FR-4.8/4.9, US-505/506).
**Orchestration**:
- `BackupService` exports encrypted, integrity-checked log snapshots periodically + on demand.
- `RecoveryService` restores from cloud replica (if reachable) or backup, rebuilds by replay, re-issues pairing QRs; spokes re-pair and replay their queues (US-502/503) to close gaps. Hot standby out of scope (D-02).

---

## Cross-cutting orchestration principles
- **Write path is always: validate → durable local write → idempotent apply → project → replicate.** No acknowledgment precedes durability (NFR-1.1).
- **The hub is authoritative** for bracket/division/schedule; spokes are authoritative only for their scoped writes; cloud is a mirror (FR-4.5).
- **Every mutation is an event**; services never mutate read models directly — they append events and let projections fold them (Q3).
- **Snowflake IDs mint at the origin** (cloud for pre-event entities, hub/spokes for event-day) and travel unchanged through replication (Q8/Q10).
