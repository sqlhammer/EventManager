# NFR Requirements Plan — U1 Shared Core

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Requirements
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24

**Inherited (already fixed — not re-asked)**: C# 13/.NET 10 (NFR-6.1); FsCheck for PBT (NFR-4.2); ≥80% coverage on sync/event-log core (NFR-4.1); append-only/auditable log (NFR-2.11); no infrastructure (U1 is a pure library — no deployment, no network, no persistence engine of its own; storage adapters live in U3/U4a). Performance envelope: 300 athletes, hub cold-start replay < 30s (NFR-5.3).

Only U1-specific technology choices remain. Answer each `[Answer]:` tag.

---

### Question 1 — Event payload serialization format
Event payloads must round-trip losslessly and replay forever (BR-1.6).

A) **System.Text.Json (source-generated)** — .NET-native, debuggable/inspectable, zero extra dependencies, comfortably fast at our scale (recommended)

B) **MessagePack** — compact fast binary; adds a dependency and is less human-readable

C) Other (please describe after [Answer]: tag below)

[Answer]: A. Defer a possible MessagePack refactor for performance optimization post-MVP.

### Question 2 — Snowflake generator implementation
Our layout is fixed (41/10/12, epoch 2026-01-01, Q8).

A) **Hand-rolled generator in `EventManager.Sync`** — full control over the exact layout + clock-regression policy, trivially unit/PBT-testable, no third-party dependency (recommended)

B) **Adopt an existing library** (e.g., IdGen) configured to our layout

C) Other (please describe after [Answer]: tag below)

[Answer]: B

> **Library option (B) = IdGen** (NuGet, MIT, de-facto .NET Snowflake lib). Our 41/10/12 layout is its default `IdStructure`; configure `IdGeneratorOptions` with custom epoch (2026-01-01) + backwards-clock strategy (`SpinWait` to wait, else throw) → maps 1:1 onto Q8. Buys proven handling of sequence-exhaustion / clock-regression / thread-safety.
> **A (hand-rolled)**: ~40 lines, no dependency, keeps the correctness core dependency-free, PBT asserts our exact invariants (BR-2.x) on our own code.
> **Recommendation: A** — U1 is the PBT correctness core and the generator is small/fixed; but B is a fine lower-effort, proven alternative that configures cleanly to our layout. **Please set Q2 = A or B.**

### Question 3 — U1 performance targets (support hub cold-start < 30s, NFR-5.3)
Confirm concrete targets for U1's hot paths.

A) **Replay/fold ≥ ~50k events in < 5s; `NextId` ≥ ~1M ids/sec single-thread; serialization round-trip sub-microsecond per event** — generous margins for 300-athlete events (recommended)

B) Different targets (specify after [Answer]: tag below)

C) Other (please describe after [Answer]: tag below)

[Answer]: A

### Question 4 — U1 test coverage gate
U1 is the correctness core and the primary PBT surface.

A) **90%+ line/branch on U1** (above the 80% baseline) given its criticality (recommended)

B) 80% baseline (NFR-4.1) is sufficient

C) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## Part 2 — Generation Checklist (executed after answers approved)

- [x] Generate `construction/u1-shared-core/nfr-requirements/nfr-requirements.md` — U1 NFRs (performance, reliability, security, maintainability, testing) traced to global NFR-x
- [x] Generate `construction/u1-shared-core/nfr-requirements/tech-stack-decisions.md` — serialization, Snowflake impl, PBT tooling, coverage gate, with rationale
- [x] Update aidlc-state.md; log approval in audit.md
