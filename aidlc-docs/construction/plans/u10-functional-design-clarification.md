# U10 Functional Design — Clarification Questions

**Stage**: CONSTRUCTION → Functional Design, Step 5 answer analysis
**Answers**: FD-Q1=C, FD-Q2=C (cap 3), FD-Q3=D, FD-Q4=D, FD-Q5=D, FD-Q6=A, FD-Q7=B, FD-Q8=C

Six answers are unambiguous. Two need something settled before rules can be written — one is a
blocker, the other is a factual correction to a premise I put in the question.

---

## CL-A — FD-Q8=C asks the hub to check something it cannot know

You chose **C**: replace an installed credential, *but refuse if the new credential is for a different
event than the one the hub currently holds data for*.

The protection is sound — pointing a hub at the wrong event is exactly the mistake worth catching
early. The problem is that **the hub cannot evaluate the condition**. A credential is an opaque key;
its event binding lives only in the cloud, in `HubCredential.EventScopeId`. The hub has no way to look
at a key and learn which event it is for.

There is a second wrinkle. "The event the hub currently holds data for" is not necessarily singular:
`admin/EventManager.Hub/Services/EventDownloadService.cs` tracks readiness per event
(`ReadinessRecord.EventId`), so a hub can in principle hold more than one downloaded event.

How should the check work?

A) **The install payload carries the event id explicitly.** `POST /api/replication/credential` takes
`{ key, cloudBaseUrl, eventId }` — the cloud returns the event id alongside the key at issue, and the
organizer supplies both. The hub compares that id against its readiness records and refuses a
mismatch. **Works with no internet at install time**, which matters because setup often happens
before connectivity. **My recommendation.**

B) **The hub asks the cloud** to resolve the credential's scope before accepting it. No extra field to
paste, and the id cannot be mistyped — but installation now requires working internet, at exactly the
moment a venue may not have it.

C) **Accept, and check at first use.** The hub stores whatever it is given; the mismatch surfaces as a
permanent failure on the first replication attempt. Simplest, and no new field — but it degrades C
back to plain "replace", which is what you chose C *over*.

D) **A, with the check relaxed when the hub holds several events** — refuse only if the credential's
event matches none of the hub's readiness records.

X) Other (please describe after [Answer]: tag below)

[Answer]: X, rollback my answer to FD-Q8=C. Instead accept FD-Q8=B.

---

## CL-B — FD-Q1=C: there is no event end date, and the grace period needs a number

You chose **C**: expiry is the event's end date plus a grace period. Correcting the premise I gave
you — **the model has no end date**. `EventRow` (`backend/EventManager.Api/Persistence/Entities.cs:27`)
carries a single `DateOnly Date`, plus `RegistrationStart` and `RegistrationEnd`. Tournaments here are
single-day.

So C reads as **`Event.Date` + grace**, which works fine and preserves your intent — the credential
cannot outlive the job it exists for. What it needs is the grace period, and that number is a real
policy choice rather than a detail: it is how long after the tournament a hub can still finish
replicating and run close-out.

A) **7 days.** Tight. Assumes the hub gets internet within a week of the event.

B) **14 days.** Comfortable for a venue that never had connectivity, without leaving credentials alive
for a month. **My recommendation.**

C) **30 days.** Generous; a credential stays usable a month after the event it belongs to.

D) **Configurable, defaulting to 14 days** — consistent with how you answered FD-Q5.

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

## Not asked — consequences of your answers I will simply write into the rules

Stated so you can object rather than discover them later.

- **FD-Q7=B** puts a nullable provenance column on `EventRecord`. Nullable is required, not a
  shortcut: cloud-originated events (registration, division configuration) are appended by
  `EventWriter` and have no ingesting credential at all, and every row that exists today predates the
  column. The rule will be *set once, at insert, only on the ingest path*.
- **FD-Q7=B under idempotent replay**: a duplicate event is skipped, not updated, so the column records
  the **first** credential that delivered an event. If a replacement hub (US-506) re-sends events the
  original hub already delivered, provenance stays with the original. That is the correct reading of
  "who delivered this first", but it is worth being explicit that it is not "who most recently sent it".
- **FD-Q2 cap of 3** counts credentials that are neither revoked nor expired. Expired credentials do
  not consume a slot, so an event that has been running for months cannot become un-issuable.
- **FD-Q3=D**, refusing close-out on an expired credential, also means the organizer cannot get a
  completeness *report*, because the report needs cloud cursors and those need a valid credential. The
  refusal message will say so and point at re-issue, rather than appearing to be a bug.
