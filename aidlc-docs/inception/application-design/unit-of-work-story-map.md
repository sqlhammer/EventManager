# Units of Work — Story Map

**Stage**: INCEPTION - Units Generation (Part 2)
**Date**: 2026-07-24
**Coverage**: All 56 stories assigned to a **primary** unit (the unit that owns delivery). "Enables/supports" lists foundational or contributing units. U1/U2 are foundational — they own no story exclusively but enable most.

---

## Epic 1 — Pre-Event Setup
| Story | Primary | Enables / supports |
|---|---|---|
| US-101 Organizer account registration | U3 | |
| US-102 Organizer login | U3 | |
| US-103 Organizer MFA | U3 | |
| US-104 Create event | U3 | |
| US-105 Configure weigh-in policy | U3 | U1 (policy model) |
| US-106 Configure divisions | U3 | U1 (division model) |
| US-107 Configure payment options | U3 | U8 |
| US-108 Add co-organizer | U3 | |
| US-109 Manage organizer roles | U3 | U1 (RBAC policy) |

## Epic 2 — Registration
| Story | Primary | Enables / supports |
|---|---|---|
| US-201 Registrant account & profile | U3 | |
| US-202 Athlete self-registration | U3 | U1 (division match) |
| US-203 Parent registers a minor | U3 | |
| US-204 Coach academy roster | U3 | |
| US-205 Coach bulk registration | U3 | |
| US-206 Bulk registration conflicts | U3 | |
| US-207 Pay-at-door election | U3 | U8 |
| US-208 Card payment (stubbed) | U8 | U3 |
| US-209 Organizer roster management | U3 | |
| US-210 Automatic division assignment | U3 | U1 (assignment logic) |
| US-211 Registrant edits registration | U3 | U1 |

## Epic 3 — Event Morning
| Story | Primary | Enables / supports |
|---|---|---|
| US-301 Download event to hub | U4a | U2 |
| US-302 Hub LAN server start | U4a | |
| US-303 Device pairing via QR | U4a | U2, U5, U6 (pairing client) |
| US-304 Pairing fallback: manual IP | U4a | U2, U5, U6 |
| US-305 Device role management & revocation | U4a | |
| US-306 Check-in | U6 | U4a (receives) |
| US-307 Weigh-in with range validation | U6 | U1 (evaluator) |
| US-308 Missed-weight policy resolution | U4b | U6 (initiates) |
| US-309 Division move regenerates bracket | U4b | U1 (bracket engine) |
| US-310 Check-in status board | U4b | U6 |
| US-311 Single-elimination bracket generation | U4b | U1 (bracket engine) |
| US-312 Round-robin generation | U4b | U1 |
| US-313 Seeding with academy separation | U4b | U1 (seeding) |
| US-314 Bracket regeneration before start | U4b | U1 |

## Epic 4 — Competition
| Story | Primary | Enables / supports |
|---|---|---|
| US-401 Mat match queue | U5 | U2 |
| US-402 Point sparring scoring | U5 | U1 (scoring) |
| US-403 Forms/kata scoring | U5 | U1 (scoring) |
| US-404 Outcome advances bracket | U4b | U1 |
| US-405 Dispute flag & resolution | U4b | U5 (flag) |
| US-406 Mat authority enforcement | U4b | U4a (credential scope) |
| US-407 Real-time spoke updates | U4a | U5, U6 (consume) |
| US-408 Mid-event organizer edits | U4b | U1 |
| US-409 Live standings on hub | U4b | |
| US-410 Cross-mat visibility | U5 | U4a (transport) |
| US-411 Match focus/lock mode | U5 | |

## Epic 5 — Offline Resilience & Recovery
| Story | Primary | Enables / supports |
|---|---|---|
| US-501 Full event with zero internet | U7 | all |
| US-502 Judge offline queue & replay | U7 | U5, U2 |
| US-503 Check-in offline queue | U7 | U6, U2 |
| US-504 Hub→cloud replication & outage replay | U7 | U3 (ingest) |
| US-505 Hub local backup export | U7 | |
| US-506 Manual hub recovery | U7 | U4a |
| US-507 Spoke auto-reconnect | U7 | U5, U6 |
| US-508 Mid-event device revocation | U4a | U7 (recovery) |

## Epic 6 — Results & Wrap-Up
| Story | Primary | Enables / supports |
|---|---|---|
| US-601 Division finalization | U4b | |
| US-602 Post-event cloud completeness | U7 | U3 |
| US-603 Registrant results & history | U3 | |

---

## Coverage summary

| Unit | Primary stories | Count |
|---|---|---|
| **U1 Shared Core** | *(foundational — enables US-105/106/109/202/210/211, all bracket/scoring/replay stories)* | 0 primary |
| **U2 Contracts & ClientSync** | *(foundational — enables pairing, spoke sync, all DTO traffic)* | 0 primary |
| **U3 Cloud Backend** | US-101–109, US-201–207, US-209–211, US-603 | 20 |
| **U4a Hub Core** | US-301–305, US-407, US-508 | 7 |
| **U4b Hub Competition** | US-308–314, US-404–406, US-408, US-409, US-601 | 13 |
| **U5 Judge App** | US-401–403, US-410, US-411 | 5 |
| **U6 Check-In App** | US-306, US-307 | 2 |
| **U7 Offline Resilience** | US-501–507, US-602 | 8 |
| **U8 Payment Stub** | US-208 | 1 |
| **Total** | | **56** |

✅ All 56 stories assigned to exactly one primary unit; every FR remains covered (see stories.md traceability matrix). Foundational units U1/U2 carry no exclusive story by design — they are verified through the stories they enable and their mandated PBT suites.
