# Unit of Work Plan — EventManager

**Stage**: INCEPTION - Units Generation (Part 1: Planning)
**Date**: 2026-07-24
**Inputs**: requirements.md, stories.md (56 stories / 6 epics), application-design artifacts (4 shared packages + 4 modules).
**Note**: This is the **last INCEPTION stage before the planned pause** — after generation & approval, we stop for your direction before CONSTRUCTION.

---

## Candidate decomposition (starting point — questions below may change it)

A "unit of work" is a logical grouping of stories that gets designed and built together, ordered by dependency. The Application Design maps cleanly to the D-07 layout; the candidate below follows it, with the sync core as the critical-path first unit.

| Unit | Contains | Nature | Heaviest concerns |
|---|---|---|---|
| **U1 — Shared Core** | `EventManager.Domain` + `EventManager.Sync` | shared libs | event log, idempotent replay, projections, Snowflake; bracket/seeding/scoring/weigh-in engines; RBAC policy — **critical path, heaviest PBT** |
| **U2 — Contracts & Client Sync** | `EventManager.Contracts` + `EventManager.ClientSync` | shared libs | wire DTOs/validators; spoke offline queue/replay/reconnect |
| **U3 — Cloud Backend** | `backend/` | service (Docker) | accounts, RBAC, registration, divisions, ingest, results |
| **U4 — Admin Hub** | `admin/` | app (hub) | hub server, pairing, download, bracket orchestration, weigh-in resolution, replication, backup/recovery, offline RBAC |
| **U5 — Judge App** | `judge/` | app (spoke) | mat queue, scoring, cross-mat, focus mode, offline |
| **U6 — Check-In App** | `checkin/` | app (spoke) | check-in, weigh-in, recommendations, status board, offline |

Suggested build order: **U1 → U2 → U3 → U4 → U5/U6** (U5 and U6 parallelizable once U2+U4 exist).

---

## Part 1 — Decomposition Questions

Answer each `[Answer]:` tag. Recommendations noted; "Other" (last option) if none fit.

### Question 1 — Shared-package unit grouping
The 4 shared packages can be grouped into units several ways.

A) **Two shared units** as above — U1 (Domain+Sync, the correctness core) and U2 (Contracts+ClientSync) — keeps the heavy-PBT core isolated and buildable first (recommended)

B) **One shared unit** — all four packages in a single "Shared Foundation" unit (fewer units, but mixes the critical-path core with lighter contract/client code)

C) **Four shared units** — each package its own unit (maximum isolation, more per-unit overhead)

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 2 — Admin Hub granularity
U4 (Admin Hub) is the largest surface (~15 stories: pairing, brackets, weigh-in resolution, replication, backup/recovery, live results, offline RBAC).

A) **Keep U4 as one unit** — cohesive (it is one deployable app) and its parts share the hub server + projections; design it as one with internal modules (recommended for a solo build)

B) **Split U4** into sub-units, e.g. U4a Hub Core (server, pairing, download, offline RBAC), U4b Hub Competition (brackets, scoring intake, weigh-in resolution, live results), U4c Hub Resilience (replication, backup, recovery) — smaller design chunks, more coordination

C) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 3 — Offline Resilience epic (E5) placement
Epic 5's 8 stories (offline queue/replay, reconnect, replication, backup, recovery) span the hub and spokes and are realized inside `Sync`/`ClientSync`/hub components.

A) **Distribute E5 stories into the units that implement them** (U1/U2 for the mechanisms, U4 for replication/backup/recovery, U5/U6 for spoke queue behavior) — no standalone unit; resilience is a property built into each unit (recommended, matches the architecture)

B) **Dedicated "Offline Resilience" unit** owning the cross-cutting behavior end-to-end

C) Other (please describe after [Answer]: tag below)

[Answer]: B

### Question 4 — Build/sequence ordering
Confirm the unit build order that drives the CONSTRUCTION per-unit loop.

A) **U1 → U2 → U3 → U4 → U5/U6** (sync core first; spokes last, parallelizable) (recommended)

B) A different order (specify)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 5 — Payment stub placement
The stubbed/mocked payment provider (D-06) is small.

A) **Part of U3 (Cloud Backend)** as an internal abstraction — no separate unit (recommended)

B) Its own small unit

C) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Part 1b — Follow-up Questions (ambiguity resolution — required before generation)

Your answers Q1=A, Q2=B, Q3=B, Q4=A, Q5=B create an **ownership overlap**: Q2=B puts hub replication/backup/recovery in **U4c (Hub Resilience)**, while Q3=B's **dedicated Offline Resilience unit** claims the same cross-cutting behavior end-to-end. Two questions resolve it.

**Provisional expanded unit set** (after B/B/B): U1 Shared Core · U2 Contracts & ClientSync · U3 Cloud Backend · U4a Hub Core · U4b Hub Competition · U5 Judge App · U6 Check-In App · **U7 Offline Resilience** · **U8 Payment Stub**.

### Question 6 — Reconcile U4c (hub resilience) vs U7 (dedicated Offline Resilience unit)
Who owns hub replication/backup/recovery, and where do the reusable sync primitives live?

A) **U7 subsumes hub resilience — drop U4c.** The hub splits into just U4a (Core: server, pairing, download, offline RBAC) + U4b (Competition: brackets, scoring intake, weigh-in resolution, live results). U7 owns the cross-cutting resilience end-to-end: hub replication/backup/recovery + spoke offline queue/replay integration + reconnect + the E5 stories + resilience PBT/integration tests. **Reusable primitives (`IEventStore`, `ReplayEngine`, `LocalEventQueue`, `ReplicationProtocol`) stay in the shared libs U1/U2** (all units depend on them); U7 owns their cross-cutting *integration and orchestration*, not the library types. (recommended — single owner, no duplication, keeps shared libs shared)

B) **Split at the hub boundary — keep both.** U4c owns hub-side resilience (hub replication/backup/recovery); U7 owns only spoke-side + shared resilience (offline queue/replay, reconnect). More units, a seam through the replication flow.

C) **Move primitives into U7.** Drop U4c; U7 owns resilience end-to-end AND the primitive library types move out of U1/U2 into U7 (makes U1/U2 lighter, but every unit now depends on U7 for core sync types).

D) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 7 — Confirm expanded build order
With the split hub, dedicated resilience unit (U7), and payment unit (U8), confirm the CONSTRUCTION per-unit sequence. Note: U8 (payment stub) is consumed by U3 registration; U7 spans U1/U2 (primitives) + U4a/U4b (hub) + U5/U6 (spokes), so it integrates after the hub exists and before/with the spokes that rely on offline queue behavior.

A) **U1 → U2 → U8 → U3 → U4a → U4b → U7 → U5 → U6** (recommended: primitives first; payment stub before the registration that uses it; hub core→competition; resilience integrates the hub then spokes consume it; U5/U6 parallelizable)

B) A different order (specify after [Answer]: tag below)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `application-design/unit-of-work.md` — unit definitions, responsibilities, contained packages/stories, and greenfield code-organization strategy (D-07 layout)
- [x] Generate `application-design/unit-of-work-dependency.md` — unit dependency matrix + build order
- [x] Generate `application-design/unit-of-work-story-map.md` — every one of the 56 stories assigned to a unit (complete coverage)
- [x] Validate unit boundaries, dependencies, and full story coverage
- [x] Update aidlc-state.md; log approval in audit.md
- [ ] **PAUSE** — stop for user direction before CONSTRUCTION (active constraint)
