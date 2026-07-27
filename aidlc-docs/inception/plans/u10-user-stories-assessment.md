# User Stories Assessment — Unit U10 HTTP Replication Adapter

**Stage**: INCEPTION → User Stories, Part 1 Step 1 (mandatory assessment)
**Date**: 2026-07-27

## Request Analysis

- **Original Request**: "HTTP replication adapter — highest value. Unblocks both integration scenarios, and both ends already exist."
- **User Impact**: **Direct.** Not what the request implied. The adapter itself is plumbing, but the answers (Q1=C, F1=B) introduced a **hub credential** that an organizer must issue, deliver to a hub, and revoke — a human workflow that does not exist anywhere in the system today.
- **Complexity Level**: **Complex** — three surfaces (`admin/`, `backend/`, Compose stack), a new authentication path, and modification of merged U7 code.
- **Stakeholders**: P1 Organizer (issues and revokes credentials, watches replication health, closes the event); the hub as a system actor; operations.

## Assessment Criteria Met

### High Priority (ALWAYS execute) — three independent hits
- [x] **New User Features** — issuing, delivering, and revoking a hub credential is new functionality an organizer directly performs. `U10-FR-2..5` have **no existing story**; the requirements traceability matrix (§9) records this gap explicitly.
- [x] **Complex Business Logic** — failure classification, circuit-breaker states, three distinct replication triggers, batch splitting, and a completeness gate are multiple interacting scenarios with rules that need testable statements.
- [x] **Customer-Facing API** — two ingest routes gain a new principal type with its own authorization semantics (`U10-FR-3`, `U10-FR-4`).

### Medium Priority
- [x] **Security Enhancements affecting authentication or permissions** — an entire new authentication path alongside the account JWT, under a blocking Security Baseline.
- [x] **Integration Work** — this unit exists to connect the hub and cloud.

### Complexity Assessment Factors
- [x] **Scope** — spans `admin/`, `backend/`, and infrastructure.
- [x] **Ambiguity** — **U10-CON-5 is unresolved**: the cloud can issue a credential and the hub can store one, but nothing connects them. That is a *user workflow* gap, which is exactly what stories are for.
- [x] **Risk** — a credential/scope error either blocks replication at a live event or grants ingest to the wrong event.
- [x] **Testing** — Q11=D chose a manual walkthrough as the primary integration verification, so testable acceptance criteria carry more weight here than usual.
- [x] **Options** — U10-CON-5 admits at least three valid delivery designs.

### Skip criteria — none apply
Not pure refactoring, not an isolated bug fix, not infrastructure-only (it adds a user-visible credential workflow), not developer tooling, not documentation.

## Decision

**Execute User Stories**: **Yes**

**Reasoning**: Three High Priority criteria are met independently, so this is not a borderline call requiring the default-to-inclusion rule. The decisive factor is `U10-FR-2..5`: the requirements document identified that no existing story covers hub identity, and the delivery path (U10-CON-5) is genuinely undecided. Writing that workflow as stories forces the decision into the open before Functional Design rather than leaving it to be improvised during code generation — which is precisely what happened with U9-CON-1 and was caught only because requirements surfaced it.

A secondary reason: Q11=D means the hub↔cloud seam's primary integration verification is a **human following a markdown walkthrough**. Acceptance criteria in Given/When/Then form are what that walkthrough will be derived from, so story quality directly determines verification quality for this unit.

## Expected Outcomes

- The U10-CON-5 credential delivery workflow is described from the organizer's point of view, so Functional Design chooses between concrete alternatives rather than inventing one.
- Replication failure behaviour (outage, revoked credential, rate limit, restart) gets testable criteria instead of living only as prose in `U10-FR-6..9`.
- The manual walkthrough required by Q11=D has an authoritative source.
- The observability answer (Q8=C / F3=B) gets a user-facing statement of what an organizer can actually see at a venue — including the honest limit recorded in U10-CON-2.
