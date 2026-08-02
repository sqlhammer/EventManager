# U10 HTTP Replication Adapter — Functional Design Plan

**Stage**: CONSTRUCTION → Functional Design (Part 1: plan + questions)
**Unit**: U10 · **Branch**: `unit/u10-http-replication`
**Inputs**: approved requirements (D-U10-01..15, U10-FR-1..19, U10-NFR-1..8, U10-CON-1..6 as amended), Epic 8 (US-801..810), Application Design (AD-Q1..Q9, CL-1, CL-2)
**Outputs**: `construction/u10-http-replication/functional-design/` — `domain-entities.md`, `business-logic-model.md`, `business-rules.md` (`BR-REPL-*`)

---

## Scope of this stage

Business logic, domain model, and rules — technology-agnostic. Component boundaries are already
settled by Application Design and are not reopened here. No frontend artifact: this unit has no UI
(the hub's MAUI shell remains a deferred seam), so `frontend-components.md` is deliberately not produced.

**Note on the questions below**: much of this unit's behaviour is already pinned. The failure
classification table is fixed by US-804, breaker defaults by D-U10-04, triggers by D-U10-05. What
remains genuinely open are the credential *policy* rules and a small number of measurement
definitions that determine whether U10-NFR-1 can be evaluated at all.

---

## PART 1a — Design Questions

Answer each by putting the letter after the `[Answer]:` tag.

---

### FD-Q1 — Who decides a credential's expiry, and what bounds it?

US-801 requires the organizer to see an expiry. It does not say who chooses it.

A) **Organizer-supplied at issue, with a hard maximum** (e.g. no more than 90 days). Flexible; the organizer must think about it every time.

B) **Fixed policy default** — every credential expires a set period after issue (e.g. 30 days), not adjustable. Simplest rule; can outlive an event by weeks, or expire mid-event for a long-running one.

C) **Derived from the event** — expiry is the event's end date plus a grace period (for post-event close-out and late replication). The credential's life matches the job it exists to do, and a credential cannot outlive its event. Requires the event to have a usable end date. **My lean.**

D) **C with an organizer override** within a hard maximum.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

### FD-Q2 — How many active credentials may one event have?

A) **Exactly one** — issuing a new credential automatically revokes the previous one. Simple to reason about; rotation is a single action; blocks any future multi-hub or hot-standby scenario.

B) **Many** — an event may have several active credentials, each independently revocable. Supports a spare hub, a replacement mid-event (US-506 manual hub recovery), and rotation without a gap. Slightly more to reason about when revoking. **My lean** — US-506 already contemplates standing up a replacement hub, and under A that would mean revoking the credential of a hub that might still be alive.

C) **Many, but capped** at a small number.

X) Other (please describe after [Answer]: tag below)

[Answer]: C, the cap is 3

---

### FD-Q3 — How is impending expiry surfaced?

US-808 says an expired credential behaves exactly like a revoked one — replication simply stops being accepted. At a live event that is a silent failure unless something warns first.

A) **Warn in replication status** when expiry is within a threshold (e.g. 7 days), visible via hub `/health` and `GET /api/replication/status`. **My lean.**

B) **No warning** — expiry is a permanent failure like any other and shows up as such when it happens.

C) **Warn, and refuse to start an event close-out** with an already-expired credential, with a clear message pointing at re-issue.

D) **A and C.**

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

### FD-Q4 — How is "the cloud is no more than 5 minutes behind" actually measured? (U10-NFR-1)

This determines whether the approved objective is testable or merely aspirational.

A) **Age of the oldest unreplicated event** — `now − OccurredAt` of the lowest un-acknowledged event. Directly expresses "how stale is the cloud"; correctly reads **zero** when there is no backlog. Requires reading that event's timestamp. **My lean.**

B) **Time since last successful replication** — cheap, but wrong when idle: with nothing to replicate, lag would climb indefinitely even though the cloud is perfectly current.

C) **Sequence gap** — count of unreplicated events rather than a duration. Easy, but not comparable to a 5-minute target.

D) **A for the objective, plus B and C reported alongside** as operational detail.

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

### FD-Q5 — Debounce and drain-timer intervals

Append-driven replication with a debounce, plus a drain timer (D-U10-05). Concrete values determine whether U10-NFR-1 holds and how chatty the hub is.

A) **Debounce 2s / drain timer 60s.** Near-real-time under load; the timer is a backstop and breaker-recovery path. Under a 5-minute target this is comfortable headroom. **My lean.**

B) **Debounce 10s / drain timer 60s.** Fewer, larger batches; still far inside the target.

C) **Debounce 2s / drain timer 30s.** Faster breaker recovery, roughly twice the idle wake-ups.

D) **Make both configurable with A as the default**, no fixed rule.

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

### FD-Q6 — How persistent is the close-out flush?

US-807 requires close-out to drive replication to completion and report whether the cloud holds everything.

A) **Bounded** — attempt for a fixed window (e.g. up to 2 minutes), then report whatever completeness was reached. Always returns; may report incomplete when the link is slow rather than broken. **My lean.**

B) **Until complete or cancelled** — keep going until everything is mirrored or the organizer cancels. Strongest guarantee; can hang at a venue with no internet, which is exactly when someone is trying to pack up.

C) **Single pass** — drain once, report. Fastest, weakest.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### FD-Q7 — Should the cloud record *which* credential ingested each batch?

SECURITY-13 asks that critical data changes be auditable — actor, timestamp, what changed. Events already carry `DeviceId`, but nothing records which principal delivered them.

A) **Record per batch** — an ingest audit row: credential id, event scope, accepted count, timestamp. Answers "which hub sent this, and when" without touching the event log. **My lean.**

B) **Record on each event record** — a column on `EventRecord`. Finest granularity; widens the hottest table in the system for audit data.

C) **Log only** — structured log lines, no persisted audit. Nothing new in the schema; the audit disappears with log retention, which is currently console-only.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### FD-Q8 — What happens when a credential is installed over an existing one?

A) **Replace** — the new credential overwrites the old, cursors are re-seeded from the cloud on the next run. Straightforward rotation. **My lean.**

B) **Refuse unless explicitly cleared first** — an install against an occupied slot is an error. Prevents an accidental overwrite of a working credential; adds a step to every rotation.

C) **Replace, but refuse if the new credential is for a different event** than the one the hub currently holds data for — protects against pointing a hub at the wrong event, which would fail at the cloud anyway but only after a confusing round trip.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Decided, stated so you can override

- **Pending-event count** (`ReplicationStatus.PendingEvents`) is computed during each replication run and is therefore *as of the last run*, not live. Making it live would mean a store query on every health probe, which AD-Q7=A rejected. It will be labelled as such rather than presented as instantaneous.
- **PBT property**: for any interleaving of outages, retries, batch splits, throttling, and restarts, the cloud log is a **gap-free prefix** of the hub log with no duplicates. This is the invariant the unit exists to preserve, and it is the one PBT-01 will be satisfied by.
- **Failure classification** is already fixed by the US-804 table; Functional Design will restate it as `BR-REPL-*` rules without changing it.
- **Breaker defaults** 3 consecutive connection failures / 60s cool-down (D-U10-04), configurable.
- **A permanent failure does not open the breaker** — it stops the current run and surfaces distinctly. Only connection failures advance the breaker.

---

## PART 1b — Resolved Decisions

*(Filled after Step 5 answer analysis. Do not proceed to Part 2 until complete.)*

| Question | Answer | Resolution |
|---|---|---|
| FD-Q1 Expiry policy | **C** + **CL-B=D** | `ExpiresAt = Event.Date + grace`, grace configurable with a 14-day default. Premise corrected: the model has no end date — `EventRow.Date` is a single `DateOnly`. Never organizer-supplied. |
| FD-Q2 Credentials per event | **C, cap 3** | At most 3 credentials that are neither revoked nor expired. Expired ones do not consume a slot. |
| FD-Q3 Expiry warning | **D** | Warn in replication status within a threshold (7-day default) **and** refuse close-out on an already-expired credential. |
| FD-Q4 Lag measurement | **D** | Objective = age of the oldest unreplicated event. Time-since-last-success and unreplicated count are reported alongside as operational detail, and are explicitly **not** the objective. |
| FD-Q5 Intervals | **D** | Configurable; defaults debounce 2s / drain timer 60s. |
| FD-Q6 Close-out persistence | **A** | Bounded window (2-minute default), then report whatever completeness was reached. |
| FD-Q7 Ingest audit | **B** | Nullable provenance column on `EventRecord`, set once at insert on the ingest path only. |
| FD-Q8 Credential replacement | **B** *(rolled back from C at CL-A)* | Install against an occupied slot is refused; clearing is an explicit separate action. |

**CL-A resolution — FD-Q8 rolled back from C to B.** C required the hub to refuse a credential
belonging to a different event, which the hub cannot evaluate: a credential is an opaque key whose
event binding exists only in the cloud. Rather than pick a workaround, the answer was withdrawn in
favour of B. Consequence, stated rather than assumed: **the wrong-event mistake is no longer caught at
install time.** It surfaces on the first replication attempt as a permanent failure, reported
distinctly from an outage per US-804 — a reasonable safety net, but later and after a round trip.

**Design amendment required by FD-Q8=B**: `ReplicationCredentialController` gains
`DELETE /api/replication/credential`. Application Design listed only POST/GET/close-out, because
under the then-current answer replacement was implicit. An explicit-clear rule needs an explicit
clear operation. `HubCredentialStore.ClearAsync` already existed in the design, so this adds a route,
not a capability.

---

## PART 2 — Execution Checklist

- [x] Re-read requirements, Epic 8, and the five Application Design artifacts
- [x] Generate `domain-entities.md` — `HubCredential`, `HubCloudCredential` (hub-side), ingest audit record if FD-Q7 requires one, and the value types used by replication state
- [x] Generate `business-logic-model.md` — credential lifecycle, replication cycle, classification and breaker state machines, close-out, cursor seeding
- [x] Generate `business-rules.md` — `BR-REPL-*` covering issuance, authentication, authorization, expiry/revocation, classification, retry, breaker, triggers, batching, completeness, status, and non-disclosure
- [x] State the PBT property formally and identify which rules it exercises
- [x] Verify every U10-FR has at least one owning business rule
- [x] Verify no rule contradicts an approved decision (D-U10-01..15, AD-Q1..Q9, CL-1, CL-2)
- [x] Record extension applicability for this stage (SECURITY-05/06/08/11/12/13/15, PBT-01, RESILIENCY-10)
- [x] Confirm no frontend artifact is required and say why
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting
- [x] Mark every checklist item [x] in the same interaction as the work
