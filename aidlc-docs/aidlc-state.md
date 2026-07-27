# AI-DLC State Tracking

## Project Information
- **Project Name**: EventManager
- **Project Type**: Greenfield
- **Start Date**: 2026-07-22T00:00:00Z
- **Current Stage**: CONSTRUCTION — **ALL 9 MVP UNITS COMPLETE & MERGED to main 2026-07-25.** U6 merged (29487fd) last. Order done: U1→U2→U8→U3→U4a→U4b→U7→U5→U6, plus refactor R1 (ternary elimination). **96 tests green** (shared 42, backend 26, admin 17, judge 6, checkin 5). Coding standard CS-1 (no ternaries) active. Remaining: optional Build-and-Test phase; Android/iOS/Mac MAUI heads (need JDK+Android SDK / Mac); real transport/SMTP/SQLCipher seams. Nothing pushed to a remote (local commits only).
- **>>> RESUME POINTER (context cleared 2026-07-25) <<<**: On `main`, working tree CLEAN, U1+U2+U8 all merged. **Start here: U3 Cloud Backend.** Create branch `unit/u3-cloud-backend` from main, then run the per-unit loop STAGE-BY-STAGE (Functional Design → NFR Requirements → NFR Design → Infrastructure Design → Code Generation) — U3 is LARGE (accounts/auth+MFA, organizer RBAC mgmt, registration self/parent/coach-bulk, division config+assignment, replication ingest [consumes U1 IEventStore + U2 EventEnvelope contracts], results; first unit with a real API layer + EF Core/PostgreSQL + Docker; consumes U8 EventManager.Payments). Recommend NOT fast-tracking. Read this file + audit.md + memory `eventmanager-aidlc-orientation` first. Tests green: 42 in shared/, 6 in backend/.
- **User Constraint (2026-07-22)**: LIFTED 2026-07-24 — user directed "proceed to construction" (approves Units Generation, ends the INCEPTION-only pause)
- **Process Requirement (2026-07-24)**: Each unit is built on its own git branch (`unit/<id>-<slug>`); all per-unit work stays on the branch until end-of-unit approval, then merges into `main`. Build order: U1 → U2 → U8 → U3 → U4a → U4b → U7 → U5 → U6
- **End-of-Unit Deliverables (2026-07-24)**: Every unit must, before end-of-unit approval/merge, (1) update architecture-overview diagrams to as-built and (2) author/update a user testing guide (manual walkthrough for UI units; developer verification guide for library units). Environment has .NET 10 SDK 10.0.302.
- **Active Branch**: `main` (U8 merged; U3 to start on `unit/u3-cloud-backend`)

## Workspace State
- **Existing Code**: No
- **Reverse Engineering Needed**: No
- **Workspace Root**: c:\repos\EventManager

## Input Documents
- aidlc-inputs/vision.md (product vision, MVP scope)
- aidlc-inputs/tech-env.md (technical environment constraints)

## Code Location Rules
- **Application Code**: Workspace root (NEVER in aidlc-docs/)
- **Documentation**: aidlc-docs/ only
- **Structure patterns**: See code-generation.md Critical Rules

## Extension Configuration
| Extension | Enabled | Mode | Decided At |
|---|---|---|---|
| Security Baseline | Yes | Full (all SECURITY rules blocking) | Requirements Analysis |
| Property-Based Testing | Yes | Full (all PBT rules blocking) | Requirements Analysis |
| Resiliency Baseline | Yes | Directional design-time guidance (all RESILIENCY rules blocking) | Requirements Analysis |

## Execution Plan Summary
- **Plan document**: aidlc-docs/inception/plans/execution-plan.md
- **Risk Level**: High (distributed offline-first event-sourcing; MAUI cross-platform TLS/SQLCipher; correctness-critical bracket/replay logic)
- **Stages to Execute**: Application Design, Units Generation (INCEPTION); Functional Design, NFR Requirements, NFR Design, Infrastructure Design, Code Generation, Build and Test (CONSTRUCTION, per-unit)
- **Stages to Skip**: Reverse Engineering (greenfield)
- **Critical path**: U1 Shared Core (event log + idempotent replay + projections) built first
- **Unit set (9)**: U1 Shared Core · U2 Contracts & ClientSync · U3 Cloud Backend · U4a Hub Core · U4b Hub Competition · U5 Judge · U6 Check-In · U7 Offline Resilience · U8 Payment Stub
- **Build order**: U1 → U2 → U8 → U3 → U4a → U4b → U7 → U5 → U6
- **PAUSE point**: after Units Generation approval, await user direction before CONSTRUCTION (active constraint)

## Stage Progress
- [x] INCEPTION: Workspace Detection (Greenfield — Reverse Engineering skipped)
- [x] INCEPTION: Requirements Analysis (approved 2026-07-22)
- [x] INCEPTION: User Stories (approved 2026-07-24 — 56 stories, 6 epics)
- [x] INCEPTION: Workflow Planning (approved 2026-07-24)
- [x] INCEPTION: Application Design (approved 2026-07-24 — 4 shared pkgs + 4 modules; D-26/D-27)
- [x] INCEPTION: Units Generation (approved 2026-07-24 — 9 units; "proceed to construction")
- [x] CONSTRUCTION: U1 Shared Core — **COMPLETE & MERGED** to main 2026-07-24 (branch `unit/u1-shared-core`; 31 tests passing)
- [ ] CONSTRUCTION: U2 Contracts & ClientSync — branch `unit/u2-contracts-clientsync` (fast-tracked; awaiting end-of-unit approval → merge)
  - [x] Functional Design (approved 2026-07-25)
  - [x] NFR Requirements / NFR Design (fast-tracked, AI-recommended) / Infrastructure Design (SKIPPED — libraries)
  - [x] Code Generation (generated + built + 42 tests passing total)
- [x] CONSTRUCTION: U2 Contracts & ClientSync — **COMPLETE & MERGED** to main 2026-07-25 (42 tests total)
- [x] CONSTRUCTION: U8 Payment Stub — **COMPLETE & MERGED** to main 2026-07-25 (c858173); stood up backend/ solution; 6 tests passing
- [ ] CONSTRUCTION: U3 Cloud Backend — **IN PROGRESS** on branch `unit/u3-cloud-backend`; large unit, stage-by-stage
  - [x] Functional Design — APPROVED (domain-entities, business-logic-model, business-rules in `construction/u3-cloud-backend/functional-design/`); answers Q1=C,Q2=A,Q3=A,Q4=A,Q5=A,Q6=A,Q7=A,Q8=N/A
  - [x] NFR Requirements — APPROVED (nfr-requirements.md, tech-stack-decisions.md); answers Q1–Q5=A, Q6=N/A
  - [x] NFR Design — APPROVED (nfr-design-patterns.md, logical-components.md); answers Q1–Q3=A, Q4=N/A
  - [x] Infrastructure Design — APPROVED (infrastructure-design.md, deployment-architecture.md); answers Q1–Q5=A, Q6=N/A
  - [x] Code Generation — **COMPLETE** (consent pre-granted). Generated `backend/EventManager.Api` (31 source files: persistence/event-store/projections/services/controllers/auth/validators/Program), EF `InitialCreate` migration, `backend/tests/EventManager.Api.Tests` (**20 tests passing** incl. PBT-1..4), infra (Dockerfile, docker-compose, Caddyfile, backup.sh, .env.example, CI). `dotnet build backend/EventManager.Backend.slnx` green. Awaiting end-of-unit review (still on branch `unit/u3-cloud-backend`; NOT yet merged; end-of-unit deliverables — arch diagram update + user testing guide — still pending per memory).
- [x] CONSTRUCTION: U3 Cloud Backend — **COMPLETE & MERGED** to main 2026-07-25 (merge b1564c8); backend/EventManager.Api + 20 tests; end-of-unit deliverables done (arch as-built + dev verification guide)
- [x] CONSTRUCTION: U4a Hub Core — **COMPLETE & MERGED** to main 2026-07-25 (merge 13945cc); `admin/EventManager.Hub` (fast-tracked) + 5 tests; pairing/device-mgmt/offline-RBAC/sync-intake/readiness; MAUI UI shell + SignalR/mDNS/SQLCipher + hub→cloud replication client deferred as seams; end-of-unit deliverables done
- [x] CONSTRUCTION: U4b Hub Competition — **COMPLETE & MERGED** to main 2026-07-25 (merge 7dbc709); `admin/EventManager.Hub/Competition` orchestrating U1 engines: bracket lifecycle, mat-authority scoring, weigh-in resolution, finalization, disputes; 12 hub tests. First unit under CS-1; end-of-unit deliverables done.
- [x] CONSTRUCTION: U7 Offline Resilience — **COMPLETE & MERGED** to main 2026-07-25 (merge 0b51346); `admin/EventManager.Hub/Resilience`: ReplicationClient + transport seam, BackupService/RecoveryService, integrating U1 ReplicationProtocol + U2 LocalEventQueue; 17 hub tests incl. zero-internet PBT.
- [x] CONSTRUCTION: U5 Judge — **COMPLETE & MERGED** to main 2026-07-25 (merge 5b992db); `judge/EventManager.Judge.Core` + compiling MAUI Windows head + 6 tests.
- [x] CONSTRUCTION: U6 Check-In — **COMPLETE & MERGED** to main 2026-07-25 (merge 29487fd); `checkin/EventManager.Checkin.Core` + compiling MAUI Windows head + 5 tests. FINAL unit — MVP unit set complete.
- [x] REFACTOR SUB-UNIT **R1 — Ternary Elimination** — **COMPLETE & MERGED** 2026-07-25. Removed all 30 ternary `?:` occurrences across shared/backend/admin (21 files) per CS-1; kept `??`/`?.`. All 80 tests green (shared 42, backend 26, admin 12). Branch `refactor/r1-ternary`.
- [x] CONSTRUCTION: Build and Test (after all units) — **COMPLETE 2026-07-27**. Artifacts in `construction/build-and-test/`: build-instructions, unit-test-instructions, integration-test-instructions, performance-test-instructions, security-test-instructions, build-and-test-summary. Build 0 warnings; **153 unit tests pass**. Integration = manual only (post-MVP per NFR-4.4), 2 scenarios blocked on the missing HTTP replication adapter. **Performance NOT executed** (no load test run). Contract tests N/A (shared library, not a versioned service API). E2E blocked (MAUI heads are template shells). **Not ready for Operations** — SBOM, CI dependency scan, coverage gate, centralized logging + alerting all outstanding.

## Post-MVP Increment — Unit U9 Read/Query API (started 2026-07-26)
- **Request**: GET endpoints for event, division, weigh-in policy, registrant, and account-with-roles (single + collection; **9 endpoints** — the weigh-in-policy collection form was removed from scope 2026-07-26, so that resource is single-only)
- **Branch**: not yet created — will be `unit/u9-read-api` per the per-unit git branch process requirement
- [x] INCEPTION: Requirements Analysis — **AWAITING APPROVAL** (`inception/requirements/u9-read-api-requirements.md`)
  - Verification answers: Q1=D, Q2=C, Q3=A, Q4=A, Q5=C, Q6=B, Q7=A, Q8=A, Q9=X, Q10=A
  - Clarification answers: C1=C (three-tier reads), C2=B (organizer roster only), C3=D (watermark ETags)
  - Q3=A superseded by C1=C (contradicted Q2=C and blocked registrant division discovery); Q5=C superseded by C2=B (blocking SECURITY-08 enumeration/IDOR finding)
  - Open design constraints carried forward: U9-CON-1 (RBAC has no read action; shared-enum change would touch U4a), U9-CON-2 (watermark ETag misses athlete-profile changes on registrant detail), U9-CON-3 (watermark validity depends on inline projection), U9-CON-4/5 (stated assumptions)
- [x] INCEPTION: User Stories — **AWAITING APPROVAL**. Part 1 planning answers Q1=A, Q2=A, Q3=B, Q4=C, Q5=B, Q6=C, Q7=C, Q8=A; clarifications C1=A (US-7xx numbering), C2=C (tier stories authoritative, resource stories shape-only). Part 2 generated **Epic 7 "Reading Event Data" — US-701..US-710** in `inception/user-stories/stories.md` (66 stories / 7 epics total) + U9-FR traceability matrix; `personas.md` gained a Persona → Read Access Tier table (no new personas; P4/P5 hold no tier). Plan: `inception/plans/u9-story-generation-plan.md` (all checklist items [x])
- [x] INCEPTION: Workflow Planning — **AWAITING APPROVAL** (`inception/plans/u9-execution-plan.md`). Risk **Medium**; rollback Easy (additive endpoints, no migration); testing Moderate
  - SKIP Application Design (no new component; U9-CON-1 bounded — **reconsider if** U9-CON-1 resolves toward extending the shared `OrganizerAction` enum, which would touch U4a)
  - SKIP Units Generation (single unit, single component)
- [x] CONSTRUCTION: Functional Design — **COMPLETE** (answers Q1=A, Q2=A, Q3=A, Q4=C). Artifacts in `construction/u9-read-api/functional-design/`: domain-entities.md, business-logic-model.md, business-rules.md (BR-READ-1..31). U9-FR-10 amended per Q3=A (inert soft-deleted-accounts clause withdrawn)
- [ ] CONSTRUCTION: NFR Requirements — **SKIP** (tech stack fixed by U3; U9-NFR-1..9 already approved; PBT-09 satisfied — FsCheck in use)
- [ ] CONSTRUCTION: NFR Design — **SKIP** (reuses U3 patterns unchanged)
- [ ] CONSTRUCTION: Infrastructure Design — **SKIP** (zero infrastructure change; U9-NFR-6 inherits U3 targets)
- [x] CONSTRUCTION: Code Generation — **COMPLETE**. 9 files created + 2 modified in `backend/`; nothing outside `backend/` touched (U9-CON-1 held). Plan `construction/plans/u9-read-api-code-generation-plan.md` all 22 steps [x]
- [x] CONSTRUCTION: Build and Test — **COMPLETE**. `dotnet build backend/EventManager.Backend.slnx` green, 0 warnings. **153 tests green** across all five solutions (shared 42, backend 83, admin 17, judge 6, checkin 5) — up from the 96 baseline, **+57 new**, zero regressions. CS-1 verified (no ternaries)
- [x] End-of-unit deliverables — as-built architecture (`inception/application-design/architecture-overview.md`, U9 section + diagram) and developer verification guide (`construction/u9-read-api/code/user-testing-guide.md`); consolidated `testing-guide.md` updated to 153
- [x] **END-OF-UNIT APPROVAL + MERGE** — approved 2026-07-27; branch `unit/u9-read-api` merged to `main`
- [x] CONSTRUCTION: **U9 Read/Query API — COMPLETE & MERGED** to main 2026-07-27. `backend/EventManager.Api` gains 9 GET endpoints under a three-tier read model (Public/Registrant/Organizer), an API-local `ReadAuthorizer` (U9-CON-1 — shared enum deliberately NOT extended, so `shared/` and `admin/` are untouched), watermark ETags hashing `(endpoint, eventId, watermark, tier, flags)`, and 404-never-403 non-disclosure. **153 tests green** (shared 42, backend 83, admin 17, judge 6, checkin 5). Postman collection updated in both representations. Not pushed to any remote.
- **Process for U9**: branch `unit/u9-read-api` from main; end-of-unit deliverables = as-built architecture diagrams + developer verification guide before merge ✅ both done

## Post-MVP Increment — Unit U10 HTTP Replication Adapter (started 2026-07-27)
- **Request**: implement the deferred hub→cloud HTTP replication seam (`ICloudReplicationTransport`), unblocking integration Scenarios 2 and 4
- **Branch**: not yet created — will be `unit/u10-http-replication` per the per-unit git branch process requirement
- [x] INCEPTION: Requirements Analysis — **APPROVED 2026-07-27** (`inception/requirements/u10-http-replication-requirements.md`)
  - Verification answers: Q1=C, Q2=C, Q3=A, Q4=C, Q5=D, Q6=B, Q7=B, Q8=C, Q9=C, Q10=D (5 min), Q11=D
  - Clarification answers: F1=B (DPAPI-wrap the credential), F2=C (append-driven + drain timer + close-out flush), F3=B (OTLP collector in the cloud Compose stack), F4=B (one in-process credential-path E2E test)
  - F1=B **closed a blocking SECURITY-12/SECURITY-01 finding** (Q1=C long-lived credential + Q2=C plaintext `hub.db` row)
  - F2=C closed a functional gap: Q5=D append-driven alone could not satisfy Q4=C breaker recovery or Q10=D close-out completeness
  - **Scope is larger than the request implied** — three surfaces (`admin/`, `backend/`, Compose stack) + modification of merged U7 code. `shared/` untouched.
  - Decisions D-U10-01..15; requirements U10-FR-1..19, U10-NFR-1..8
  - Open design constraints carried forward: U10-CON-1 (DPAPI makes the hub library Windows-only in code — needs an `ISecretProtector` seam), U10-CON-2 (cloud-side collector is blind during the outages the unit exists to survive), U10-CON-3 (rate limit points at our own hub — needs a concrete number), U10-CON-4 (first `admin/`→`backend/` project reference), U10-CON-5 (**credential has no delivery path** — hub MAUI UI is still a seam; Functional Design must choose), U10-CON-6 (modifies merged U7 `ReplicationClient`)
  - **Infrastructure Design is NOT skippable** for this unit (F3=B adds a Compose service)
- [x] INCEPTION: User Stories — **AWAITING APPROVAL**. Assessment `inception/plans/u10-user-stories-assessment.md` (Execute = Yes, three High Priority criteria). Plan `inception/plans/u10-story-generation-plan.md`, all 26 Part 2 checklist items [x]
  - Planning answers chosen by the AI at user direction ("proceed with your recommendations"): Q1=A, Q2=A, Q3=B, Q4=B, Q5=A, Q6=B, Q7=C, Q8=A, Q9=A, Q10=A — each recorded with reasoning in the plan's PART 1b table so any can be reversed
  - Part 2 generated **Epic 8 "Hub Identity & Cloud Replication" — US-801..US-810** in `inception/user-stories/stories.md` (76 stories / 8 epics total) + a U10-FR/NFR traceability matrix
  - **US-504 and US-602 amended** with delivery notes: both were satisfied by U7 only in-process, never over a real network (Q6=B)
  - No new persona (Q9=A); `personas.md` gained a U10 note and an extended Story Map
  - U10-NFR-7 deliberately maps to no story (inherits U3 targets, nothing new for an organizer to observe)
- [x] INCEPTION: Workflow Planning — **AWAITING APPROVAL** (`inception/plans/u10-execution-plan.md`). Risk **High**; rollback **Moderate** (EF migration); testing **Complex**
  - EXECUTE (7): Application Design, Functional Design, NFR Requirements (minimal — tech-stack selection only), NFR Design, Infrastructure Design, Code Generation, Build and Test
  - SKIP (1): Units Generation (one coherent unit; the hub half is useless without the cloud half)
  - **Application Design EXECUTES** (unlike U9) — new components in both solutions, and U10-CON-5 is a component-interaction decision this stage owns
  - **Infrastructure Design is mandatory** — F3=B adds a Compose collector service (SECURITY-07/02, RESILIENCY-05)
  - 10-step package change sequence; `shared/` explicitly unchanged; the `ReplicationClient` edit is isolated as its own step because it is the only change to merged U7 code
  - Fast-track option documented (collapse Application Design + NFR Requirements + NFR Design into Functional Design → 4 stages), matching the U4a/U4b/U5/U6/U7 pattern; stage-by-stage still recommended

## Post-MVP Untracked Work (backfilled 2026-07-26)
- **Account self-deletion (US-110)** — `DELETE /api/accounts/me` endpoint, `AccountDeletionService`/`AccountDeletionGuard`, EF migration `AccountSoftDelete`, `AccountDeletionTests` — implemented directly on `main` (commit 7159038, merged via PR #1 / 56b2c3e) on 2026-07-25, **without** a `unit/<id>-<slug>` branch, per-unit stage gates, or audit.md logging, in violation of the per-unit git branch process requirement (line 10 above). Functionally complete and merged; retroactively logged here for traceability. No further action taken.
- **"Web portal" tech-stack update** (commit d9aa82c, 2026-07-26) — edits to `aidlc-inputs/vision.md` and `aidlc-inputs/tech-env.md` introducing a new Blazor web portal (`EventManager.Web`) as a second delivery surface. This is an input-document change only — no unit exists for it yet, no code generated. **Not** registered as a unit in the Unit set / build order above; flagged here so it isn't mistaken for tracked scope. Left as-is per user direction (2026-07-26) — registering it as a formal unit is a separate future decision.
