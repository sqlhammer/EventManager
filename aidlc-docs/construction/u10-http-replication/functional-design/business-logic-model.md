# U10 — Business Logic Model

**Unit**: U10 HTTP Replication Adapter · **Stage**: Functional Design
Technology-agnostic behaviour. Rule identifiers refer to `business-rules.md`.

---

## 1. Credential lifecycle

```text
        issue (organizer with rights on the event)
              │  BR-REPL-1..6
              ▼
          ┌────────┐   now >= ExpiresAt    ┌─────────┐
          │ ACTIVE │ ────────────────────► │ EXPIRED │
          └────────┘                       └─────────┘
              │                                 │
              │ revoke (BR-REPL-15)             │
              ▼                                 │
          ┌─────────┐                           │
          │ REVOKED │ ◄─────────────────────────┘  (revocable while expired;
          └─────────┘                                effect is already identical)
```

`EXPIRED` and `REVOKED` are **behaviourally identical** — both are permanent authentication failures,
never retried (BR-REPL-14). They are kept distinct only so an organizer can tell "it ran out" from
"someone turned it off", which are different things to do something about.

State is derived from timestamps rather than stored, so it cannot drift.

---

## 2. Provisioning (US-801, US-802)

```text
ISSUE   organizer ──JWT──► cloud
        ├─ rights on this event?              BR-REPL-1
        ├─ fewer than 3 active credentials?   BR-REPL-5
        ├─ generate key, hash it, store hash  BR-REPL-2, BR-REPL-3
        ├─ ExpiresAt = Event.Date + grace     BR-REPL-4
        └─ return the key ONCE                BR-REPL-2

INSTALL organizer ──hub organizer auth──► hub
        ├─ slot occupied?  → REFUSE           BR-REPL-21
        ├─ base URL HTTPS (or dev override)?  BR-REPL-25
        ├─ protect, then persist              BR-REPL-22
        └─ status: CredentialInstalled = true

CLEAR   organizer ──hub organizer auth──► hub
        └─ remove; replication becomes a no-op with a stated reason
```

Install refuses rather than overwrites (FD-Q8=B). The cost is a two-step rotation; the benefit is
that a working credential cannot be destroyed by a careless paste.

**Known gap, accepted at CL-A**: nothing checks at install time that the credential belongs to the
event this hub holds. The hub only ever sees an opaque key. A mismatch surfaces on the first
replication attempt as a permanent failure, reported distinctly from an outage.

---

## 3. Authentication and authorization at ingest

```text
request ──► hash presented key ──► lookup
                                     │
             not found / expired / revoked ──► generic failure   BR-REPL-7
                                     │                            (discloses nothing)
                                  active
                                     ▼
                       principal = (CredentialId, EventScopeId)
                                     ▼
         batch scopes ⊆ { EventScopeId } ?  ──no──► refuse ENTIRE batch  BR-REPL-10
                                     │
                                    yes
                                     ▼
                     idempotent append; provenance recorded  BR-REPL-18..20
```

Evaluated **per request** — there is no session and no cache, which is what makes revocation take
effect on the very next attempt (BR-REPL-8).

Partial acceptance is rejected on purpose (BR-REPL-10): a batch is the unit of atomic intent, and
accepting the in-scope half of a mixed batch would leave the hub's cursor arithmetic describing
something that did not happen.

---

## 4. The replication cycle (US-803, US-804)

```text
wake on: append signal (debounced) │ drain timer │ close-out request
              BR-REPL-36..38
                     │
      credential installed? ──no──► no-op, reason reported        BR-REPL-24
                     │
      circuit closed? ──────no──► no-op, not an error             BR-REPL-35
                     │
                     ▼
        next batch above each device cursor   (≤500 envelopes, ≤ byte cap; split)  BR-REPL-27
                     │
                     ▼
                   send ─────────────────────────────────┐
                     │                                   │
                 success                              failure
                     │                                   │
        advance cursors from ack                    classify   BR-REPL-28..31
        BR-REPL-41                                       │
                     │                    ┌──────────────┼───────────────┐
                     │            transient-connection  transient/throttled  permanent
                     │                    │              │                  │
                     │            advance breaker   backoff or Retry-After   stop run,
                     │            BR-REPL-33..34     then retry              surface
                     │                    │              │              distinctly
                     │                    └──────────────┘              BR-REPL-32
                     ▼
        backlog remaining? ──yes──► loop
                     │
                    no ──► update status + metrics
```

Two distinctions carry real weight:

- **Only connection failures advance the breaker** (BR-REPL-33). A `500` means the cloud is reachable and unwell — a different situation from a dead venue link, and opening the breaker on it would suppress retries that would have succeeded.
- **A permanent failure does not consume retry attempts and does not open the breaker.** It stops the run and is reported as its own thing, because the operator's response differs: an outage needs waiting, a revoked credential needs a new credential (US-804).

---

## 5. Cursor seeding at startup (US-805)

```text
start ──► fetch cloud high-water marks
             ├─ success ──► seed cursors, resume from the cloud's real position
             └─ failure ──► start anyway with empty cursors        BR-REPL-40
                            (re-sending is idempotent: wasteful, never incorrect)
```

Non-blocking by design. A hub must be able to start at a venue with no internet — making seeding a
startup prerequisite would turn a connectivity problem into a hub that will not run.

---

## 6. Close-out (US-807)

```text
close-out requested
     │
  credential expired? ──yes──► REFUSE, name re-issue          BR-REPL-17
     │                          (completeness report is unavailable —
     │                           it needs cloud cursors)
     no
     ▼
  replicate until backlog empty OR bounded window elapses     BR-REPL-39
     │
     ▼
  completeness: for every device, cloud HWM >= local HWM      BR-REPL-42
     │
     ├─ complete   ──► "fully replicated — N events"
     └─ incomplete ──► outstanding count; NOT reported as complete
```

Bounded rather than open-ended (FD-Q6=A): close-out happens while someone is packing up, and a call
that never returns at a venue with no internet is worse than an honest incomplete answer.

---

## 7. Lag and status (US-806)

**The objective** (U10-NFR-1, FD-Q4=D): `lag = now − OccurredAt` of the **oldest unreplicated
event**; zero when there is no backlog (BR-REPL-44).

Reported alongside as operational detail, explicitly *not* the objective (BR-REPL-45):
time since last successful replication, and count of unreplicated events.

Time-since-last-success was rejected as the objective because it is wrong precisely when the system
is healthiest: with nothing to replicate it climbs indefinitely while the cloud is perfectly current.

All status is computed in-process and never requires reaching the cloud (BR-REPL-47) — otherwise the
one question an organizer asks during an outage would be unanswerable during an outage. The pending
count is as of the last replication run, not live (BR-REPL-46), and is labelled that way.

---

## 8. Property to be verified (PBT-01)

**P-REPL-1** — For any interleaving of outages, connection failures, throttling, batch splits,
permanent failures, and hub restarts: the cloud's event log is, per device, a **gap-free prefix** of
the hub's log, and contains **no duplicates**.

This is the invariant the entire unit exists to preserve, and it is what makes the flagship
zero-data-loss claim (NFR-1.1) true over a real network rather than only in-process. It exercises
BR-REPL-10, -19, -27, -32, -40, -41, -42, -43.
