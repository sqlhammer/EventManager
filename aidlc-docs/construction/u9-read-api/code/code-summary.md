# Code Summary — Unit U9: Read/Query API

**Branch**: `unit/u9-read-api`
**Completed**: 2026-07-26
**Build**: `dotnet build backend/EventManager.Backend.slnx` — succeeded, 0 warnings
**Tests**: **153 green** across all five solutions (baseline was 96 — **+57 new**)

| Solution | Tests | Change |
|---|---|---|
| `shared/EventManager.Shared.slnx` | 42 | unchanged |
| `backend/EventManager.Backend.slnx` | 83 (77 API + 6 payments) | **+57** |
| `admin/EventManager.Admin.slnx` | 17 | unchanged |
| `judge/EventManager.Judge.slnx` | 6 | unchanged |
| `checkin/EventManager.Checkin.slnx` | 5 | unchanged |

---

## Files created

### Production — `backend/EventManager.Api/`
| File | Purpose |
|---|---|
| `Auth/ReadAuthorizer.cs` | `AccessTier` enum + the API-local read authorizer (U9-CON-1). Single-event resolution in 3 indexed lookups; collection resolution in 4 queries independent of result size |
| `Services/ReadEtagProvider.cs` | Watermark lookup and opaque ETag construction; `If-None-Match` matching incl. weak tags and `*` |
| `Contracts/ReadContracts.cs` | Seven response records — summary, detail, list item, division, weigh-in policy, registrant list/detail, organizer account |
| `Services/EventQueryService.cs` | Event collection with tier tagging; single event with tier-driven shape |
| `Services/DivisionQueryService.cs` | Division list/single, `includeCompleted`, cross-scope check |
| `Services/WeighInPolicyQueryService.cs` | Single effective policy; tolerance omitted unless mode is Tolerance |
| `Services/RegistrantQueryService.cs` | Roster (Organizer-only, minimal) and detail (Organizer any / Registrant own) |
| `Services/OrganizerAccountQueryService.cs` | Organizer roster and single lookup, email joined from the identity plane |
| `Controllers/EventReadController.cs` | All nine GET endpoints, tier resolution, ETag policy, denial logging |

### Tests — `backend/tests/EventManager.Api.Tests/`
| File | Covers |
|---|---|
| `ReadTierTests.cs` | US-701/702/703 — qualification, cumulative grants, withdrawal fallback, Full Admin/Co-Organizer parity, per-event tiers, collection tagging |
| `ReadShapeTests.cs` | US-704..708 — shapes, inclusion flags, tolerance rule, deleted-account registrations, unpaginated collections |
| `ReadNonDisclosureTests.cs` | US-709 — 404 parity, no 403 anywhere, cross-event probing for divisions/registrations/accounts |
| `ReadEtagTests.cs` | US-710 — watermark advance, tier sensitivity, flag sensitivity, opacity, `If-None-Match`, and the U9-CON-2 demonstration |
| `ReadPropertyTests.cs` | PBT properties P1–P6 |

## Files modified
| File | Change |
|---|---|
| `backend/EventManager.Api/Program.cs` | Registered the seven new scoped services |
| `backend/tests/EventManager.Api.Tests/TestHost.cs` | Exposed the read components; added `RegisterAsync` and `SeedIdentityAsync` helpers |

**Nothing outside `backend/` was touched** — the U9-CON-1 decision kept `shared/`, `admin/`,
`judge/`, and `checkin/` entirely out of this unit.

---

## Endpoints delivered

| # | Endpoint | Min. tier | ETag |
|---|---|---|---|
| 1 | `GET /api/events` | any | ❌ cross-scope |
| 2 | `GET /api/events/{eventId}` | Public | ✅ |
| 3 | `GET /api/events/{eventId}/divisions` | Public | ✅ |
| 4 | `GET /api/events/{eventId}/divisions/{divisionId}` | Public | ✅ |
| 5 | `GET /api/events/{eventId}/weigh-in-policy` | Public | ✅ |
| 6 | `GET /api/events/{eventId}/registrants` | Organizer | ✅ |
| 7 | `GET /api/events/{eventId}/registrants/{registrationId}` | Organizer, or Registrant for own | ❌ U9-CON-2 |
| 8 | `GET /api/events/{eventId}/accounts` | Organizer | ✅ |
| 9 | `GET /api/events/{eventId}/accounts/{accountId}` | Organizer | ✅ |

---

## Design decisions worth knowing

### The ETag covers the tier, not just the watermark
The most consequential implementation detail. A watermark-only ETag would have shipped a real bug:
a caller who gained a tier — by registering for an event they had only browsed — would present
their old `If-None-Match` and receive **304 Not Modified while still holding the narrower Public
body**, silently withholding data they had just become entitled to. The token therefore hashes
`(endpoint, eventId, watermark, tier, flags)`. `Etag_differs_per_tier_at_the_same_watermark` and the
property `Etag_is_tier_sensitive_at_a_fixed_watermark` pin this.

### No read endpoint returns 403
Insufficient tier is always 404, identical to "does not exist". A 403 confirms the resource exists,
which is precisely the disclosure US-709 forbids. This is a deliberate departure from the write
controllers, which do use 403 — there the caller already demonstrably knows the resource exists.
Enforced by returning `Error.NotFound` rather than `Error.Forbidden`, since `ApiControllerBase`
maps Forbidden to 403.

### Registrant detail is deliberately uncacheable
`ReadEtagTests.Profile_edit_does_not_move_the_event_watermark_so_detail_is_uncached` demonstrates
the U9-CON-2 hazard directly: after a real weight change, the event-scoped watermark is unchanged.
An ETag built from it would still match a stale `If-None-Match` and serve the old weight. Rather
than paper over it, the endpoint sets no ETag at all (Q1=A).

### Two layers of authorization
The controller resolves and checks the tier; every query service re-checks the tier it was handed
and refuses to run if it is insufficient. No single control is the sole line of defence
(SECURITY-11).

---

## Extension compliance

**Security Baseline** — SECURITY-03 (denial logging, no PII), SECURITY-05 (validated params,
parameterized EF queries throughout), SECURITY-08 (deny-by-default, object-level authorization,
no existence disclosure), SECURITY-11 (authorization isolated in `ReadAuthorizer`, layered checks,
account-enumeration abuse case addressed), SECURITY-12 (no credential material in any response),
SECURITY-15 (fail closed) — **all compliant**. SECURITY-01/02/04/06/07/09/10/13/14 are inherited
from U3 or N/A — no new store, network surface, dependency, or HTML endpoint.

**Property-Based Testing** — PBT-01 properties identified in functional design and implemented as
P1–P6; PBT-03 invariants; PBT-04 idempotence (P4); PBT-05 oracle (P3); PBT-07 domain generators for
status and tier rather than raw primitives; PBT-09 FsCheck already in use; PBT-10 PBT sits alongside
example-based tests, and no critical path relies on PBT alone. PBT-02 (round-trip) and PBT-06
(stateful) are **N/A** — this unit performs no serialization round-trip and holds no mutable state.

**Resiliency Baseline** — inherited from U3 unchanged. No new workload, store, or deployment
surface, so RESILIENCY-08..13 are N/A for this unit.

**CS-1** — verified: no ternary `?:` in any new production or test file.

---

## Deferred / not built
- No client consumes these endpoints yet (Q1=D — the Blazor portal is not in scope)
- `GET /api/events` has no ETag; if a polling client ever appears, a per-caller composite watermark
  would be the natural addition
- Registrant detail caching remains open by design — revisit only if profile events gain event-scoped
  fan-out
