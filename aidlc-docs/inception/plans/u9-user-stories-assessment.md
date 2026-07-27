# User Stories Assessment — Unit U9: Read/Query API

**Created**: 2026-07-26
**Stage**: INCEPTION → User Stories, Step 1 (mandatory assessment)

## Request Analysis

- **Original Request**: GET endpoints for event, division, weigh-in policy, registrant, and
  account-with-roles (single + collection per scope). Narrowed at requirements approval to
  **9 endpoints** — the weigh-in-policy collection form was removed from scope.
- **User Impact**: **Direct** — the endpoints determine what each class of user can see about an
  event, including what a prospective registrant can discover before registering
- **Complexity Level**: **Medium** — no new domain logic or persistence, but a genuinely new
  three-tier authorization model (T0 public / T1 registrant / T2 organizer) that does not exist in
  the system today
- **Stakeholders**: P1 Organizer, P2 Coach, P3 Registrant (existing personas). No new personas.

## Assessment Criteria Met

- [x] **High Priority — Multi-Persona Systems**: the tier model produces materially different
      behaviour for three personas against the same endpoints. Stories are the natural place to pin
      per-persona expectations.
- [x] **High Priority — Customer-Facing APIs**: these are externally consumed read endpoints.
- [x] **High Priority — Complex Business Logic**: tier resolution is cumulative and per-event, with
      distinct response shapes per tier and explicit non-disclosure rules.
- [x] **Medium Priority — Security Enhancements affecting permissions**: the unit's substance *is*
      an authorization model. The requirements stage already caught one blocking SECURITY-08 finding
      (unrestricted account lookup); story-level acceptance criteria are where the remaining
      negative cases get pinned so they cannot be quietly dropped during code generation.
- [x] **Complexity — Testing**: acceptance testing per tier is required, and the Property-Based
      Testing extension is enabled and blocking. Story acceptance criteria feed PBT-01 property
      identification at Functional Design.
- [x] **Complexity — Ambiguity**: requirements carry two unresolved design constraints (U9-CON-1
      RBAC read action, U9-CON-2 watermark ETag gap) plus two stated assumptions (U9-CON-4 weigh-in
      policy tier, U9-CON-5 registrant self-reads). Stories make the user-visible half of those
      decisions concrete.

## Decision

**Execute User Stories**: **Yes**

**Reasoning**: This clears the High Priority bar on three independent criteria, so the assessment is
not a borderline call. The decisive factor is that the same nine endpoints behave differently for
three personas — that matrix is exactly what stories express well and what a bare endpoint list
expresses badly. A second factor is enforcement: with Security Baseline blocking, the negative
cases (what a caller must *not* see, and the requirement that denial be indistinguishable from
absence) need to be written as testable acceptance criteria rather than left as prose in the
requirements document.

This is explicitly **not** one of the skip cases: it is neither pure refactoring, nor an isolated
bug fix, nor infrastructure-only, nor tooling, nor documentation.

## Expected Outcomes

- A per-tier, per-resource behaviour matrix that Functional Design can implement against directly
- Testable negative-case criteria for SECURITY-08 (non-disclosure, IDOR resistance) that survive
  into code generation rather than being re-derived
- Candidate invariants for PBT-01 property identification at Functional Design
- Explicit user-visible framing for the two stated assumptions (U9-CON-4, U9-CON-5), so approving
  the stories is a real decision on them rather than a silent inheritance
