# Requirements Verification Questions — Proposed Unit U9: Read/Query API

**Stage**: INCEPTION → Requirements Analysis
**Request**: GET endpoints for event, division, weigh-in policy, registrant, and account-with-roles (single + collection per scope)
**Created**: 2026-07-26

Please answer each question by putting a letter after the `[Answer]:` tag. If none of the
options fit, pick the last option (Other) and describe what you want after the tag.

---

## Context the AI already established (no need to answer, just for your review)

- `backend/EventManager.Api` today is **write-only except one endpoint** — the sole GET is
  `GET /api/results/athletes/{athleteId}`. Everything else is POST/PUT/DELETE.
- The read models these GETs would serve **already exist** and are populated by the cloud
  projection host: `EventRow`, `DivisionRow`, `RegistrationRow`, `OrganizerRow`,
  `AthleteProfileRow`, `ResultRow`.
- Event RBAC already exists (`EventAuthorizer` → U1 `RoleAuthorizationPolicy`, deny-by-default)
  but it only models **organizer** actions (`OrganizerAction` enum). There is **no read action**
  in that enum and no notion of a non-organizer (registrant/parent/coach) read permission.
- Weigh-in policy is currently **not a separate entity**. It is two columns on `EventRow`
  (`WeighInPolicyMode`, `WeighInTolerancePercent`) — exactly one policy per event.

---

## Question 1
Who is the primary consumer driving this read API right now?

A) The new Blazor **web portal** (`EventManager.Web`) introduced in the recent tech-stack update — these GETs are the data layer it will bind to

B) The **admin hub** (`EventManager.Hub`) — it needs to pull event/division/registrant data down from the cloud before going offline

C) **Both** the web portal and the admin hub, plus general API completeness

D) No specific client yet — build the read surface for API completeness and future clients

X) Other (please describe after [Answer]: tag below)

[Answer]: D

---

## Question 2
"All events **that the user has access to**" — how should "access" be defined?

A) **Organizer access only** — events where the caller holds a Full Admin or Co-Organizer role

B) **Organizer OR participant** — events where the caller is an organizer, *or* has registered themselves/an athlete they manage

C) **Organizer, participant, or publicly listed** — the above, plus any event with registration open, so people can discover events to register for

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 3
Who may read **divisions and event detail** for an event?

A) **Organizers only** — every read endpoint requires an organizer role on that event (deny-by-default, same rule as writes)

B) **Organizers + registrants** — anyone with a registration in the event can also read event detail and divisions; only organizers see the full roster

C) **Public read for event + divisions** — anyone authenticated can read event/division info (needed to browse and pick divisions before registering); registrant and account lists stay organizer-only

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Question 4
You asked for weigh-in policy "single **and all** for the event", but the current model has exactly
**one** weigh-in policy per event (two columns on the event row). How should this be handled?

A) **Read-only over today's model** — `GET /api/events/{id}/weigh-in-policy` returns the single
   effective policy; the "all" form returns a one-item collection. No data model change.

B) **Per-division policy overrides** — introduce division-level weigh-in policies that override the
   event default, so "all for the event" is genuinely a list. This is a **new domain capability**
   (new events, projection, and write endpoints) beyond a read-only unit.

C) **Effective-policy-per-division view** — no model change, but the "all" form returns the
   *resolved* policy for each division (all identical today, but the shape is future-proof)

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Question 5
"Account (single and all **with roles for the event**)" — what exactly should this return?

A) **Organizer roster only** — the accounts holding an organizer role on the event, each with its
   `OrganizerRole` (Full Admin / Co-Organizer). Single form = one organizer's assignment.

B) **Organizer roster + the caller's own account** — the above, plus `GET /api/accounts/me` so a
   client can render the signed-in user's profile

C) **Any account by id, plus its roles on the event** — a general account lookup scoped by event,
   readable by organizers of that event

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Question 6
How much personal data should the **registrant** endpoints expose?

A) **Full registration record to organizers** — athlete name, academy, divisions, payment status,
   assignment-mismatch flags; plus profile fields (date of birth, weight, rank, gender) since
   organizers need them for weigh-in and division checks

B) **Minimal by default, detail on the single-item endpoint** — the list returns name/academy/
   divisions/payment status only; date of birth, weight, and contact data appear only on
   `GET .../registrants/{id}`

C) **Redacted for Co-Organizers** — Full Admins see everything; Co-Organizers get an age band and
   weight class instead of raw date of birth and weight

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Question 7
Do the collection endpoints need pagination, filtering, and sorting?

A) **Return the full collection** — event-scale data is small (hundreds of registrants); keep it simple

B) **Filtering + sorting only** — e.g. registrants by division / payment status / withdrawn,
   divisions by status; but always return the whole filtered set

C) **Full pagination** — cursor or page/size on every collection, plus filtering and sorting

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Question 8
How should withdrawn registrations, completed divisions, and soft-deleted accounts appear?

A) **Excluded by default, opt in via query flag** (e.g. `?includeWithdrawn=true`)

B) **Always included, with a status field** — the client decides what to show

C) **Always excluded** — withdrawn/deleted records are simply not readable through this API

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Question 9
Should responses support HTTP caching / conditional requests (`ETag` + `If-None-Match` → 304)?

A) **No** — plain 200 responses; the hub and portal always fetch fresh

B) **Yes, ETags on single-item endpoints only** — cheap win for detail views

C) **Yes, ETags on everything**, plus `Cache-Control` guidance per endpoint

X) Other (please describe after [Answer]: tag below)

[Answer]: X. Explain the pros and cons to me before I choose.

---

## Question 10
Three extensions are currently enabled for this project (Security Baseline, Property-Based Testing,
Resiliency Baseline — all "Full / blocking" per `aidlc-state.md`). Do they carry over to this unit?

A) **Yes, all three carry over unchanged** — read endpoints get the same blocking security,
   property-based-testing, and resiliency treatment

B) **Security + PBT carry over; Resiliency is N/A** for a read-only surface

C) **Security only** — this is a read surface with no new domain logic

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

**When you have answered all 10, tell me you're done** and I will check the answers for
contradictions, then produce the requirements document for your approval.
