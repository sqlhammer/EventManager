# Business Logic Model — Unit U9: Read/Query API

**Stage**: CONSTRUCTION → Functional Design
**U9-CON-1 decision**: **API-local read authorizer** — the shared `OrganizerAction` enum is not
extended; nothing in `shared/` or `admin/` is touched by this unit.

---

## 1. Component map

```text
Controllers  ─┐
              ├─> ReadAuthorizer ──> AppDbContext (OrganizerRow, RegistrationRow, EventRow)
              │      resolves AccessTier per event
              │
              ├─> Query services ──> AppDbContext (read-model rows)
              │      EventQueryService, DivisionQueryService,
              │      WeighInPolicyQueryService, RegistrantQueryService,
              │      OrganizerAccountQueryService
              │
              └─> ReadEtagProvider ──> AppDbContext (EventRecord watermark)
```

`ReadAuthorizer` is the single place tier decisions are made (SECURITY-11 separation of concerns).
No query service performs its own authorization; each receives an already-resolved tier and refuses
to run if the tier is insufficient.

---

## 2. Tier resolution

### Single event

```text
ResolveAsync(callerAccountId, eventId) -> AccessTier

1. event <- EventRows.Find(eventId)
   if event is null            -> None
2. if OrganizerRows has (eventId, callerAccountId)
                               -> Organizer
3. if RegistrationRows has (EventId = eventId,
       ManagedByAccountId = callerAccountId, Withdrawn = false)
                               -> Registrant
4. if event.RegistrationStatus = Open
                               -> Public
5. otherwise                   -> None
```

Highest match wins; the order of checks encodes the tier ordering. Three indexed lookups at worst,
each on an existing index (`OrganizerRow` unique `(EventId, AccountId)`; `RegistrationRow` on
`EventId`; `EventRow` primary key).

### Collection — bounded queries, no N+1 (U9-NFR-9)

`GET /api/events` resolves tiers for every visible event in **four queries, independent of result
size**:

```text
1. organizerIds  <- OrganizerRows.Where(AccountId = caller).Select(EventId)
2. registrantIds <- RegistrationRows.Where(ManagedByAccountId = caller,
                                           Withdrawn = false).Select(EventId).Distinct()
3. publicIds     <- EventRows.Where(RegistrationStatus = Open).Select(EventId)
4. rows          <- EventRows.Where(EventId in union(1,2,3))

tier(e) = Organizer  if e in organizerIds
        = Registrant if e in registrantIds
        = Public     otherwise
```

The per-event tier is then computed in memory from three id sets — no query per event.

---

## 3. Shape selection

```text
ShapeFor(tier) = Summary  when tier = Public
               = Detail   when tier in { Registrant, Organizer }
```

Shape is derived from the resolved tier alone, never from the endpoint or a client parameter. A
client cannot request a richer shape than its tier allows, because the shape is not an input.

---

## 4. Query services

| Service | Minimum tier | Reads | Notes |
|---|---|---|---|
| `EventQueryService` | Public | `EventRow` | Collection tags each item with tier + organizer role |
| `DivisionQueryService` | Public | `DivisionRow` | Excludes `Status = Complete` unless `includeCompleted` |
| `WeighInPolicyQueryService` | Public | `EventRow` | Projects the two policy columns; single form only |
| `RegistrantQueryService` | Organizer (list), Organizer or owning Registrant (detail) | `RegistrationRow`, `AthleteProfileRow` | Excludes withdrawn unless `includeWithdrawn` |
| `OrganizerAccountQueryService` | Organizer | `OrganizerRow`, `AppUser` | Email joined from the identity record by `AccountId` |

Every service is passed the resolved `AccessTier` and fails closed if it is insufficient — a second
layer behind the controller check (SECURITY-11 defence in depth).

---

## 5. ETag derivation

### Watermark
`MAX(EventRecord.EventId) WHERE EventScopeId = {eventId}` — one lookup on the existing index at
[AppDbContext.cs:36](../../../../backend/EventManager.Api/Persistence/AppDbContext.cs#L36).
`EventId` is a monotonic Snowflake, so the maximum is a true version token.

The watermark is **exact**, not approximate, because `CloudProjectionHost` folds read models in the
same transaction and `DbContext` as the append
([EventWriter.cs:33-36](../../../../backend/EventManager.Api/Events/EventWriter.cs#L33-L36)) — there
is no projection lag for it to miss.

### The ETag must cover everything that determines the body

A watermark alone is **not sufficient**. The same event at the same watermark produces different
bodies for different tiers and different inclusion flags. If the ETag ignored those, a caller who
gained a tier — say, by registering for the event — would present their old `If-None-Match` and
receive a **304 telling them nothing changed, while holding the narrower Public body**. That is a
correctness bug and, because it withholds data the caller is now entitled to, a confusing one to
diagnose.

So:

```text
etag = opaque_hash( endpointIdentity, eventId, watermark, resolvedTier, inclusionFlagValues )
```

### Opacity
The ETag is a hash rendered as an opaque quoted token — **never the raw watermark**. A bare
Snowflake would leak event-log volume and the timestamp of the last activity on an event to any
caller holding the public tier.

### Coverage

| Endpoint | ETag | Reason |
|---|---|---|
| Event single, division single/list, weigh-in policy, registrant **list**, account single/list | ✅ | Fully event-scoped; watermark is exact |
| `GET /api/events` | ❌ | Spans multiple event scopes; no single watermark (C3=D) |
| Registrant **detail** | ❌ | **Q1=A** — reads `AthleteProfileRow`, mutated by events scoped to the *athlete*, not the event ([RegistrationService.cs:43-44](../../../../backend/EventManager.Api/Services/RegistrationService.cs#L43-L44)). The event watermark would not move on a weight edit, so a 304 could carry a stale weight. Excluding the endpoint is provably correct and closes U9-CON-2 |

A conditional hit must answer 304 **without querying read-model tables** — only the watermark
lookup and the hash (U9-NFR-1).

### U9-CON-3 — validity depends on inline projection
This design is correct only while projection stays synchronous and inline. If projections ever
become asynchronous, the watermark must switch to a projection-applied high-water mark. This is to
be recorded as a comment at the watermark source so a future change cannot silently break caching
correctness.

---

## 6. Error model

| Condition | Status | Rationale |
|---|---|---|
| No authenticated principal | **401** | Deny by default; no anonymous tier exists |
| Caller resolves to tier `None` | **404** | Indistinguishable from a nonexistent event |
| Event id does not exist | **404** | Same body as above — no existence disclosure |
| Resource exists but belongs to another event | **404** | Cross-scope probing yields nothing |
| Tier insufficient for the endpoint (e.g. Public asking for the roster) | **404** | Not 403 |
| Malformed id or flag value | **400** | Input validation precedes data access (SECURITY-05) |
| Unhandled failure | **500**, generic body | Fail closed; no stack trace or internal detail (SECURITY-09, SECURITY-15) |

**No read endpoint in this unit ever returns 403.** A 403 confirms that the resource exists, which
is precisely the disclosure US-709 forbids. This is a deliberate departure from the write endpoints,
which do use 403 — there, the caller already demonstrably knows the resource exists.

---

## 7. Testable properties (PBT-01)

| Property | Category | Statement |
|---|---|---|
| **P1 — Deny by default** | Invariant | For all generated (caller, event) pairs where the caller holds no organizer role, no non-withdrawn registration, and the event is not Open: every endpoint returns 404 with an empty body |
| **P2 — Shape confinement** | Invariant | For all events, a `Public` response contains no detail-tier field and no roster field |
| **P3 — Query equivalence** | Oracle | For all generated row sets, each query service's output equals a naive in-memory filter over the same rows |
| **P4 — Conditional stability** | Idempotence | For all entitled reads, repeating an identical request while the watermark is unchanged returns an identical body and an identical ETag |
| **P5 — Tier monotonicity** | Invariant | For all callers and events, adding a registration or an organizer role never lowers the resolved tier, and never removes a field that was previously visible |
| **P6 — Cross-scope isolation** | Invariant | For all division, registration, and account ids not belonging to event E, requesting them under E returns 404 |

**Generators** (PBT-07): domain generators for `EventRow` (valid date ordering, non-negative fee),
`DivisionRow` (coherent ranges), `RegistrationRow`/`AthleteProfileRow` (paired so the BR-REG-8
ownership invariant holds), and `OrganizerRow`. No raw primitive generators for domain-typed values.

**Components with no PBT property**: the controllers themselves — they contain no logic beyond
delegation, and their behaviour is fully covered by example-based endpoint tests (PBT-10).
