# EventManager — Personas

Five personas. The combined Athlete/Parent persona is named **Registrant** (user decision, story plan Q5).

---

## P1 — Organizer (dojo owner)

**Profile**: Owner/head instructor of an independent dojo running 1–4 tournaments a year, 50–300 athletes. Solo or with a tiny volunteer crew. Moderately tech-comfortable; zero tolerance for tools that fail mid-event.

**Goals**
- Set up an event (divisions, fees, schedule) in an evening, not a week
- Run the whole event day even if venue WiFi/internet dies
- Keep athletes, coaches, and parents informed without being the bottleneck

**Authority**: An event can have multiple Organizers via RBAC (D-20): **Full Admin** and **Co-Organizer**. Both can create/edit brackets/divisions/schedule, resolve disputes, assign/revoke devices, and finalize results. Full Admin additionally can delete the event, remove/demote other organizers, and grant/transfer Full Admin. The event's creator is Full Admin by default; organizers added later default to Co-Organizer. Runs the Admin app, which *is* the LAN hub — whichever organizer is authenticated on the Admin device exercises their role's permissions.

**Offline context**: Admin device is the source of truth on event day; cloud is a mirror.

**Frustrations**: Cloud-only competitors dying with venue WiFi; per-athlete pricing; manual bracket paperwork.

---

## P2 — Coach

**Profile**: Instructor at a participating academy bringing 5–40 athletes. Registers on behalf of their students, often the night before the deadline.

**Goals**
- Bulk-register a roster in one sitting; reuse the roster across events
- See their athletes' divisions, mat assignments, and results

**Authority**: Cloud account; owns their academy roster; can register/withdraw their own athletes during the registration window. No event-day administrative authority.

**Offline context**: Interacts pre-event via cloud (online). On event day, has no privileged device role in MVP.

---

## P3 — Registrant (athlete or parent of a minor athlete)

**Profile**: Adult athlete registering themselves, or a parent managing one or more minor athletes under their own account. Combined persona — flows are shared; the parent variant manages multiple athlete profiles.

**Goals**
- Register for the right divisions quickly; pay online or elect pay-at-door
- Edit registration details before the window closes; see registration history and results

**Authority**: Cloud account; owns own profile(s) and registrations only.

**Offline context**: Cloud-only interactions in MVP (spectator LAN experience is Phase 2).

---

## P4 — Judge / Scorekeeper

**Profile**: Volunteer black belt or experienced student assigned to one mat. Uses a phone/tablet with the Judge app. May have never used the app before event morning — pairing and scoring must be learnable in minutes.

**Goals**
- See each of the mats` match queue, in order.
- Device can be locked/focused on a single match
- Enter scores/outcomes fast, with confidence nothing is lost — even fully offline
- Flag a disputed match and move on

**Authority**: Device-paired write role scoped to one assigned mat — authoritative only for that mat's scores/outcomes. No cloud account required; identity comes from device enrollment (QR pairing). Can read-only other mats, if an active connection is available at the time.

**Offline context**: Fully operational against the hub with no internet; queues events locally if the hub is briefly unreachable.

---

## P5 — Check-In / Weigh-In Staff

**Profile**: Volunteer at the entry table on event morning, handling a line of athletes. Speed and clarity dominate.

**Goals**
- Mark athletes present in seconds; record weights with instant in/out-of-range feedback
- Hand policy exceptions (missed weight) to the organizer without holding up the line

**Authority**: Device-paired Check-In role; **append-only** — can add check-ins/weigh-ins, never modify brackets or scores. Policy resolution (DQ/move/tolerance) is organizer authority. Can recommend for DQ/move/tolerance where the organizer can easily see the recommendations.

**Offline context**: Same as Judge — hub-local operation, local queueing on disconnect.

---

## Persona → Story Map

| Persona | Primary stories |
|---|---|
| P1 Organizer | US-101..109, US-209, US-301..305, US-308..314, US-405, US-408, US-409, US-501, US-504..506, US-508, US-601, US-602 |
| P2 Coach | US-204, US-205, US-206, US-207 (shared), US-603 (shared) |
| P3 Registrant | US-201, US-202, US-203, US-207, US-208, US-210 (system-facing, on behalf of), US-211, US-603 |
| P4 Judge | US-303, US-304, US-401..407, US-410, US-411, US-502, US-507 |
| P5 Check-In Staff | US-303, US-304, US-306, US-307, US-308 (initiates), US-310, US-503, US-507 |

Stories with system-level behavior (e.g., US-210 auto division assignment, US-504 cloud replication) are written from the persona that benefits/observes.
