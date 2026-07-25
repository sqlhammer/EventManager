# Functional Design Plan — U2 Contracts & ClientSync

**Stage**: CONSTRUCTION - U2 Contracts & ClientSync - Functional Design
**Branch**: `unit/u2-contracts-clientsync`
**Date**: 2026-07-24
**Unit**: U2 = `EventManager.Contracts` (DTOs + validators, single source of truth for REST + LAN APIs) + `EventManager.ClientSync` (spoke-side offline behavior: local queue, sync client, reconnect, push consumer, pairing client).
**Depends on**: U1 (done) — `EventManager.Sync` (`TournamentEvent`, `IEventStore`, `IIdGenerator`) and `EventManager.Domain`.

Note the ordering wrinkle: U2 is built **before** its API consumers (U3 backend, U4a hub). So contracts are defined **contract-first** from requirements/stories. The questions below settle how far to take that now.

---

## Part 1 — Questions

### Question 1 — Contract scope at U2 (contract-first vs grow-with-consumer)
How complete should `EventManager.Contracts` be now?

A) **Transport-level contracts now; domain DTOs grow with their consumer.** Define now what ClientSync + replication actually need — the wire **event envelope**, **pairing** payloads, **hub→spoke push** messages, **replication batch** — plus the validation seam. Domain request/response DTOs (registration, event, results) are added to Contracts when U3/U4a are built. (recommended — avoids speculative rework, still single-source once added)

B) **Full DTO set now** — define every REST + LAN DTO (auth, event, registration, division, organizer, scoring, check-in, results, pairing, replication) up front from the stories.

C) **Only what ClientSync needs now** — pairing + event envelope + push; defer all other contracts entirely.

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 2 — ClientSync durability seam
The spoke offline queue must persist events durably before ack (NFR-1.1). Where does durability live?

A) **Reuse U1's `IEventStore`** — `LocalEventQueue` writes to an injected `IEventStore` (the spoke app provides the SQLite/SQLCipher adapter in U5/U6); ClientSync owns queue/replay orchestration, not storage (recommended — one storage seam, consistent with U1)

B) **ClientSync owns its own queue store abstraction** separate from `IEventStore`

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 3 — Hub→spoke push consumption model
How does `HubPushConsumer` surface pushed updates (bracket/schedule/results) to the app?

A) **Typed event stream / observable** the app subscribes to (`IObservable`-style or callback registration); ClientSync applies to a local projection and raises change notifications (recommended)

B) **Raw message callback only** — app handles everything

C) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `construction/u2-contracts-clientsync/functional-design/domain-entities.md` — contract DTOs + validation rules in scope; ClientSync state (queue item, sync status, connection state)
- [x] Generate `construction/u2-contracts-clientsync/functional-design/business-logic-model.md` — queue-durable-before-ack, replay-on-reconnect, backoff loop, pairing handshake, push apply
- [x] Generate `construction/u2-contracts-clientsync/functional-design/business-rules.md` — validation rules + ClientSync invariants (no-loss, idempotent replay, honest sync status) with PBT candidates
- [x] (frontend-components.md — N/A: libraries)
- [x] Update aidlc-state.md; log approval in audit.md
