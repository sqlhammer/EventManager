# Requirements Clarification Questions — Proposed Unit U9: Read/Query API

**Stage**: INCEPTION → Requirements Analysis (clarification round)
**Created**: 2026-07-26
**Source answers**: `u9-read-api-verification-questions.md` (Q1=D, Q2=C, Q3=A, Q4=A, Q5=C, Q6=B, Q7=A, Q8=A, Q9=X, Q10=A)

Three items need resolution before I can write the requirements document. Answer each with a
letter after the `[Answer]:` tag.

---

## Contradiction 1: Public event discovery (Q2=C) vs. organizers-only reads (Q3=A)

You chose:

- **Q2=C** — "all events I have access to" includes **publicly listed** events (registration open),
  *"so people can discover events to register for"*
- **Q3=A** — event detail and divisions are **organizers only**, deny-by-default, same rule as writes

These cannot both hold. Under Q3=A a non-organizer would receive an event in the collection from
`GET /api/events` and then get **403 on `GET /api/events/{id}`** for that same event — the list
advertises resources the caller is forbidden to open.

**This also breaks the existing registration flow.** `POST /api/registration` requires
`DivisionIds` ([RegisterRequest → RegisterInput](../../../backend/EventManager.Api/Controllers/RegistrationController.cs)).
If divisions are organizer-only readable, a registrant has **no way to discover a division id**, so
nobody outside the organizer team can ever complete a registration through the API.

### Clarification Question 1
How should read authorization actually work?

A) **Organizers only, everywhere — including the list.** Revise Q2 to option A: `GET /api/events`
   returns only events where the caller holds an organizer role. No public discovery. Registrant
   division discovery is deferred to a future unit. *(Internally consistent; smallest unit; leaves
   self-registration unusable via API until a later unit adds it.)*

B) **Two-tier reads.** Event summary + divisions are readable by any authenticated caller for events
   with registration open (public tier); full event detail, registrant lists, and account/role lists
   stay organizer-only (privileged tier). *(Matches your Q2=C intent and keeps registration working;
   requires adding a read action to the RBAC model.)*

C) **Three-tier reads.** Public tier as in B; registrants additionally read full detail for events
   they are registered in; organizer tier as in B. *(Most faithful to real usage; largest authz
   surface to design and test.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Ambiguity 2: Account lookup scope (Q5=C) — blocking under SECURITY-08

You chose **Q5=C** — *"any account by id, plus its roles on the event, readable by organizers of
that event."*

Read literally, this lets any organizer probe `GET /api/events/{theirEvent}/accounts/{anyAccountId}`
for **arbitrary account ids**, including accounts with no connection to their event. That is an
account-enumeration and PII-disclosure vector: an organizer learns which account ids exist and their
associated identity data.

The **Security Baseline extension is enabled and blocking** (your Q10=A). **SECURITY-08** requires
object-level authorization — *"every request that references a resource by ID MUST verify the
requesting user has permission to access that resource (prevent IDOR)."* An unrestricted account
lookup would be a non-compliant finding that blocks the unit from completing.

### Clarification Question 2
Which accounts should be readable through the event-scoped account endpoint?

A) **Accounts related to the event only** — the caller may read an account by id if that account
   holds an organizer role on the event **or** has a registration in it. Unrelated ids return 404
   (not 403, so existence is not disclosed). *(SECURITY-08 compliant; still satisfies "any account
   by id" for every id an organizer legitimately deals with.)*

B) **Organizer roster only** — revert to Q5=A: only accounts holding an organizer role on the event
   are readable. *(Narrowest surface.)*

C) **Truly any account by id**, accepting the enumeration risk, mitigated by rate limiting and
   audit logging of every lookup. *(Requires you to accept a documented SECURITY-08 exception.)*

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Question 9 revisited: ETags / conditional requests — pros and cons

You asked for the trade-offs before choosing. Here is what I found in **this** codebase.

### The key technical fact
**No read-model row has a version or last-modified column.** `EventRow`, `DivisionRow`,
`RegistrationRow`, `OrganizerRow`, `AthleteProfileRow` in
[Entities.cs](../../../backend/EventManager.Api/Persistence/Entities.cs) carry no `RowVersion` or
`UpdatedAt`. So a naive ETag has to be a **hash of the serialized response** — the server still runs
the query and serializes the result, then throws it away on a 304. That saves **bandwidth only, not
database or CPU work**, which is the weakest form of the win.

### But there is a good option specific to this architecture
This is an event-sourced system. `EventRecord.EventId` is a **monotonic Snowflake**, and there is
already an index on `EventScopeId`
([AppDbContext.cs:36](../../../backend/EventManager.Api/Persistence/AppDbContext.cs#L36)).
So `MAX(EventId) WHERE EventScopeId = {eventId}` is a **single cheap indexed lookup that acts as a
true version watermark** for everything scoped to that event. Any event-scoped read (event,
divisions, weigh-in policy, registrants, organizer roster) can produce a strong ETag from that
watermark and answer a 304 **without touching the read-model tables at all**. That is a real saving,
and it falls out of the architecture you already have.

The exception: `GET /api/events` ("all events I can access") spans multiple scopes, so it has no
single watermark and would fall back to response hashing.

### Trade-offs

| | Pros | Cons |
|---|---|---|
| **A — No ETags** | Simplest; smallest test surface; payloads are already small since you chose Q7=A (no pagination) | Repeat polling always costs a full query + serialize; nothing to build on if the hub later syncs frequently |
| **B — Single-item only** | Cheap win on detail views; low complexity | Detail responses are the *smallest* payloads — least benefit; collections (registrant roster) are where bandwidth actually goes |
| **C — Everywhere** | Best for a future offline-first hub doing repeated pre-event pulls; watermark approach makes event-scoped 304s genuinely cheap | Most complexity; the cross-scope event list needs a different mechanism; more to test under a blocking PBT extension |
| **D — Watermark, event-scoped** *(new option)* | Captures nearly all the benefit of C at much lower cost; uses an index that already exists; conceptually clean in an event-sourced system | Still more complex than A; the event-list endpoint gets no ETag |

### My recommendation
**Option D**, or **A** if you want the unit as small as possible. You answered **Q1=D — no specific
client yet**, which genuinely weakens the case for caching: the main beneficiary would be an
offline-first hub doing repeated pulls, and that client does not exist yet. Choosing A now and
adding watermark ETags when a real consumer appears is a defensible call and costs little later,
because the watermark query does not depend on anything this unit builds.

### Clarification Question 3
Which caching approach?

A) **No ETags** — plain 200s

B) **ETags on single-item endpoints only**, via response hashing

C) **ETags on everything**, plus `Cache-Control` guidance

D) **Watermark ETags on event-scoped endpoints** (single + collection), derived from
   `MAX(EventId)` per `EventScopeId`; no ETag on the cross-scope event list

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

## Noted, no answer needed

Your **Q7=A** (return the full collection, no pagination) and **Q8=A** (exclude withdrawn/completed/
deleted by default, opt in via a query flag) are compatible. I will read them as: **no pagination and
no general-purpose filtering or sorting**, but the specific inclusion flags from Q8 are supported
(`?includeWithdrawn=true` and equivalents). Say so if you meant something different.

---

**Answer the three questions above and tell me you're done**, and I will write the requirements
document for your approval.
