# AI-DLC State Tracking

## Project Information
- **Project Name**: EventManager
- **Project Type**: Greenfield
- **Start Date**: 2026-07-22T00:00:00Z
- **Current Stage**: CONSTRUCTION — **U4a merged to main 2026-07-25 (merge 13945cc). Next unit: U4b Hub Competition.** U1 merged 2026-07-24; U2+U8+U3+U4a merged 2026-07-25.
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
- [ ] CONSTRUCTION: U4b Hub Competition — **NEXT** (branch `unit/u4b-hub-competition`); depends on U4a + U1 (bracket/scoring engines already in U1)
- [ ] CONSTRUCTION: U7, U5, U6 (per build order, each on its own branch)
- [ ] CONSTRUCTION: Build and Test (after all units)
