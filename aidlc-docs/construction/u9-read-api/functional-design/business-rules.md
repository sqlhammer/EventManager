# Business Rules — Unit U9: Read/Query API

**Stage**: CONSTRUCTION → Functional Design
Each rule is stated so it can be asserted directly by a test.

---

## Tier qualification

| ID | Rule | Source |
|---|---|---|
| **BR-READ-1** | Deny by default. Absent an explicit tier grant, a caller receives no data from any endpoint in this unit. | U9-FR-2, SECURITY-08 |
| **BR-READ-2** | Tiers are totally ordered `None < Public < Registrant < Organizer` and cumulative. A caller holds the highest tier they qualify for on that event, and it confers every lower tier's grants. | US-701..703 |
| **BR-READ-3** | A caller holds `Organizer` on an event iff an `OrganizerRow` exists for `(eventId, callerAccountId)`. `OrganizerRole` does **not** differentiate read access — Full Admin and Co-Organizer receive identical responses. | US-703 |
| **BR-READ-4** | A caller holds `Registrant` on an event iff a `RegistrationRow` exists with that `EventId`, `ManagedByAccountId == caller`, and `Withdrawn == false`. A caller whose only registration is withdrawn does not hold `Registrant`. | US-702 |
| **BR-READ-5** | A caller holds `Public` on an event iff `EventRow.RegistrationStatus == Open`. **The registration date window does not affect discoverability** — an event left Open past its window remains discoverable, and the window is returned in the payload so clients can present it as expired (**Q4=C**). | US-701, Q4=C |
| **BR-READ-6** | Tier is resolved **per event**. No tier is global; the same caller may hold `Organizer` on one event and `Public` on another. | US-703 |

## Response shape

| ID | Rule | Source |
|---|---|---|
| **BR-READ-7** | Response shape is a function of resolved tier alone: `Public` → summary, `Registrant`/`Organizer` → detail. Shape is never selectable by a client parameter. | U9-FR-2 |
| **BR-READ-8** | The registrant **list** is `Organizer`-only and carries no date of birth, weight, rank, or gender. | U9-FR-5, US-707 |
| **BR-READ-9** | Registrant **detail** is readable by `Organizer` for any registration in the event, and by `Registrant` only where `ManagedByAccountId == caller`. It adds date of birth, weight, rank, and gender. | U9-FR-6, US-702 |
| **BR-READ-10** | Account endpoints are `Organizer`-only and read `OrganizerRow` exclusively. An account id holding no organizer role on the path event returns 404. | U9-FR-7, US-708 |
| **BR-READ-11** | Account responses carry `accountId`, `email`, and `role` only. No password hash, MFA secret, recovery code, or session token is ever returned. | US-708, SECURITY-12 |
| **BR-READ-12** | Weigh-in policy returns `mode`, and `tolerancePercent` only when `mode == Tolerance`. Exactly one policy per event; no collection form exists. | U9-FR-8, US-706 |

## Collections and exclusions

| ID | Rule | Source |
|---|---|---|
| **BR-READ-13** | Collections return the complete result set. No pagination, no page-size parameter, and no general-purpose filter or sort parameter is accepted. | U9-FR-9, Q7=A |
| **BR-READ-14** | Withdrawn registrations are excluded by default and included via `?includeWithdrawn=true`, where they remain identifiable as withdrawn. | U9-FR-10, Q8=A |
| **BR-READ-15** | Divisions with `Status == Complete` are excluded by default and included via `?includeCompleted=true`. | U9-FR-10, Q8=A |
| **BR-READ-16** | **Registrations whose managing account has been deleted are shown normally** — deletion of an account does not remove or flag its athletes' entries. The roster describes who is competing, not account lifecycle state. | **Q2=A** |
| **BR-READ-17** | There is **no** soft-deleted-account exclusion or inclusion flag. Account deletion appends `OrganizerRemoved` for every role held and the projection deletes the `OrganizerRow`, so a deleted account can never appear in an organizer roster. The clause originally in U9-FR-10 was inert and has been withdrawn. | **Q3=A** |

## Non-disclosure

| ID | Rule | Source |
|---|---|---|
| **BR-READ-18** | A caller resolving to tier `None`, and a caller requesting an event id that does not exist, receive **the same 404 with the same body**. The two cases are indistinguishable. | U9-FR-12, US-709 |
| **BR-READ-19** | A resource id that is valid but belongs to a different event returns 404 when requested under the path event — never 403, never the resource. | U9-FR-4, US-709 |
| **BR-READ-20** | **No read endpoint in this unit returns 403.** A 403 confirms the resource exists, which is the disclosure US-709 forbids. Insufficient tier is always 404. | US-709 |
| **BR-READ-21** | Every authorization denial is logged with acting account, event id, and endpoint. Log entries contain no personal data. | U9-NFR-5, SECURITY-03, SECURITY-14 |

## Caching

| ID | Rule | Source |
|---|---|---|
| **BR-READ-22** | The ETag is an opaque hash over `(endpoint, eventId, watermark, resolvedTier, inclusionFlags)`. It must cover every input that determines the body — a watermark alone would let a caller who gained a tier receive a 304 while holding the narrower body. | U9-FR-11, U9-NFR-1 |
| **BR-READ-23** | The raw watermark is never exposed. A bare Snowflake would leak event-log volume and last-activity timing. | SECURITY-08 |
| **BR-READ-24** | A conditional request whose ETag matches returns 304 with no body and **without querying read-model tables**. | U9-NFR-1 |
| **BR-READ-25** | `GET /api/events` issues no ETag — it spans multiple event scopes with no single watermark. | C3=D |
| **BR-READ-26** | **Registrant detail issues no ETag.** It reads `AthleteProfileRow`, which is mutated by events scoped to the athlete rather than the event, so the event watermark would not move on a profile edit and a 304 could carry a stale weight. Excluding the endpoint closes U9-CON-2. | **Q1=A**, US-710 |

## Validation and failure

| ID | Rule | Source |
|---|---|---|
| **BR-READ-27** | Path and query parameters are validated for type and permitted values before any data access. Malformed input returns 400. | U9-NFR-4, SECURITY-05 |
| **BR-READ-28** | All queries are parameterized through EF Core. No raw SQL is composed from user input. | U9-NFR-3, SECURITY-05 |
| **BR-READ-29** | Fail closed. Any failure during tier resolution denies access; it never falls through to a permissive default. | U9-NFR-7, SECURITY-15 |
| **BR-READ-30** | Unhandled failures return a generic 500 with no stack trace, internal path, or database detail. | SECURITY-09, SECURITY-15 |
| **BR-READ-31** | Query services receive an already-resolved tier and refuse to execute if it is insufficient — a second check behind the controller's, so no single control is the sole line of defence. | SECURITY-11 |

---

## Requirement coverage

| Requirement | Rules |
|---|---|
| U9-FR-1 | BR-READ-7..12 |
| U9-FR-2 | BR-READ-1, 2, 6, 7 |
| U9-FR-3 | BR-READ-5, 6, 13 |
| U9-FR-4 | BR-READ-19 |
| U9-FR-5 | BR-READ-8 |
| U9-FR-6 | BR-READ-9 |
| U9-FR-7 | BR-READ-10, 11 |
| U9-FR-8 | BR-READ-12 |
| U9-FR-9 | BR-READ-13 |
| U9-FR-10 | BR-READ-14, 15 (soft-deleted-account clause withdrawn — BR-READ-17) |
| U9-FR-11 | BR-READ-22..26 |
| U9-FR-12 | BR-READ-18, 19, 20 |
| U9-NFR-1..9 | BR-READ-21..31 |

## Coding standard
**CS-1** applies to all code generated for this unit: no ternary `?:` operator. `??` and `?.` remain
permitted.
