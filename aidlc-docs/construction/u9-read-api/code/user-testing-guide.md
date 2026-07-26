# User Testing Guide — Unit U9: Read/Query API

**Type**: Developer verification guide (backend unit — no UI)
**Branch**: `unit/u9-read-api`
**Prerequisite**: .NET 10 SDK. Docker only for the live-API walkthrough in §3.

---

## 1. Fast verification — run the tests

```bash
dotnet build backend/EventManager.Backend.slnx
dotnet test  backend/EventManager.Backend.slnx
```

Expected: **83 passing** (77 API + 6 payments). Full regression across every solution:

```bash
dotnet test shared/EventManager.Shared.slnx     # 42
dotnet test backend/EventManager.Backend.slnx   # 83
dotnet test admin/EventManager.Admin.slnx       # 17
dotnet test judge/EventManager.Judge.slnx       #  6
dotnet test checkin/EventManager.Checkin.slnx   #  5
```

**Total 153.** The pre-U9 baseline was 96, so U9 adds 57 and changes nothing existing.

### What each test file proves

| File | Read it to confirm |
|---|---|
| `ReadTierTests` | Tier qualification and the cumulative model — including that a **withdrawn** registration drops the caller back to Public, and that Full Admin and Co-Organizer read identically |
| `ReadShapeTests` | Public gets summary and never detail; inclusion flags; tolerance only under Tolerance mode; a deleted account's athletes stay on the roster |
| `ReadNonDisclosureTests` | The security core — 404/404 parity, no 403 anywhere, cross-event probing blocked for divisions, registrations, and accounts |
| `ReadEtagTests` | ETag changes on write, per tier, and per flag; never exposes the raw watermark; and the U9-CON-2 demonstration |
| `ReadPropertyTests` | Properties P1–P6 over generated inputs |

---

## 2. The three checks worth doing by hand

These are the behaviours most likely to be got wrong by a future change.

### 2.1 A stranger can discover an open event but not its roster

```
Given  an event with registration Open
When   an account with no role and no registration reads it
Then   200 with the SUMMARY shape (no cardEnabled / checkInStarted / createdByAccountId)
And    GET .../registrants returns 404 — not 403
```
Covered by `Open_event_grants_public_tier_to_a_stranger` and
`Registrant_list_is_organizer_only_and_omits_profile_fields`.

### 2.2 Denial is indistinguishable from absence

```
Given  a real event the caller holds no tier on, and an event id that never existed
When   both are requested
Then   both return 404 with byte-identical error code and description
```
This is the SECURITY-08 property. Covered by `No_tier_and_unknown_event_return_identical_errors`
and `Unrelated_and_nonexistent_account_probes_are_indistinguishable`.

### 2.3 Gaining a tier invalidates a cached response

```
Given  a caller who read an open event at Public tier and kept the ETag
When   they register for the event and re-read with If-None-Match
Then   they must NOT receive 304 — the ETag covers the tier, so it has changed
```
The subtle one. A watermark-only ETag would return 304 here and silently withhold the detail
fields the caller just earned. Covered by `Etag_differs_per_tier_at_the_same_watermark` and the
property `Etag_is_tier_sensitive_at_a_fixed_watermark`.

---

## 3. Live API walkthrough

```bash
cd backend
cp .env.example .env          # if not already present
docker compose up -d
```

Register and log in two accounts — an organizer and a stranger — using the existing endpoints
(`POST /api/accounts/register`, `POST /api/accounts/confirm-email` with the token from the email
outbox table, `POST /api/accounts/login`). Then, as the organizer, create an event, add a division,
and open registration using the U3 write endpoints.

With `$ORG` and `$STRANGER` holding the two bearer tokens:

```bash
# Organizer sees detail + roster
curl -H "Authorization: Bearer $ORG" localhost:8080/api/events
curl -H "Authorization: Bearer $ORG" localhost:8080/api/events/$EVENT
curl -H "Authorization: Bearer $ORG" localhost:8080/api/events/$EVENT/registrants
curl -H "Authorization: Bearer $ORG" localhost:8080/api/events/$EVENT/accounts

# Stranger sees the summary only — the last two must be 404, NOT 403
curl -H "Authorization: Bearer $STRANGER" localhost:8080/api/events/$EVENT
curl -H "Authorization: Bearer $STRANGER" localhost:8080/api/events/$EVENT/divisions
curl -i -H "Authorization: Bearer $STRANGER" localhost:8080/api/events/$EVENT/registrants
curl -i -H "Authorization: Bearer $STRANGER" localhost:8080/api/events/$EVENT/accounts

# Conditional request: capture the ETag, then replay it
ETAG=$(curl -sI -H "Authorization: Bearer $ORG" localhost:8080/api/events/$EVENT | grep -i '^etag:' | cut -d' ' -f2 | tr -d '\r')
curl -i -H "Authorization: Bearer $ORG" -H "If-None-Match: $ETAG" localhost:8080/api/events/$EVENT
# expect: 304 Not Modified

# Registrant detail deliberately carries NO ETag header
curl -I -H "Authorization: Bearer $ORG" localhost:8080/api/events/$EVENT/registrants/$REG
```

### Expected results

| Request | Organizer | Stranger |
|---|---|---|
| `GET /api/events` | includes the event, `accessTier: "Organizer"` | includes it, `accessTier: "Public"` |
| `GET /api/events/{id}` | detail shape | summary shape |
| `GET .../divisions` | 200 | 200 |
| `GET .../weigh-in-policy` | 200 | 200 |
| `GET .../registrants` | 200 | **404** |
| `GET .../accounts` | 200 | **404** |
| Unknown event id | **404** | **404** |

---

## 4. Behaviour to be aware of

**Deleting your account does not remove your athletes from an organizer's roster.** Account
deletion (US-110) anonymizes the identity record only — it does not withdraw registrations or scrub
athlete profiles. An athlete registered by a since-deleted parent or coach account keeps their name,
date of birth, and weight in the roster, because they are still competing and the organizer needs to
see them on the mat. This was decided deliberately (Functional Design Q2=A) and is worth stating to
users who expect deletion to be a full erasure.

**An event left Open past its registration window stays publicly discoverable.** Discoverability
keys off registration *status*, not the date window (Q4=C). The window is returned in the payload so
a client can render the event as expired. If an organizer wants an event to disappear from public
listings, they must close registration.

**Registrant detail is never cached.** No ETag, no 304 — always a fresh read. See §2.3 and
U9-CON-2 for why.

---

## 5. Known limits

- No client consumes these endpoints yet — the Blazor portal was explicitly out of scope (Q1=D)
- `GET /api/events` issues no ETag; it spans event scopes with no single watermark
- Collections are complete and unpaginated by decision (Q7=A). At tournament scale — hundreds of
  registrants — this is fine; a roster in the tens of thousands would want revisiting
- The ETag watermark is correct only while projection stays synchronous and inline (U9-CON-3). If
  projections ever go asynchronous, switch to a projection-applied high-water mark
