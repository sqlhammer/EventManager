# U1 Shared Core — Tech Stack Decisions

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Requirements
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24

---

## Decisions

### TSD-1 — Language/runtime (inherited)
C# 13 / .NET 10, `net10.0` class libraries. No platform-specific APIs so the packages are consumable by MAUI clients and the ASP.NET backend alike (NFR-6.1).

### TSD-2 — Event payload serialization (Q1=A)
**System.Text.Json with source generation** (`JsonSerializerContext`). .NET-native, zero extra runtime dependency, debuggable/inspectable payloads, ample performance at our scale.
- Round-trip lossless (BR-1.6); property-tested.
- **Deferred (post-MVP)**: a MessagePack refactor if payload size/throughput ever needs it — the `IEventSerializer` seam is defined so this is a drop-in swap, not a rewrite.

### TSD-3 — Snowflake ID generation (Q2=B — IdGen)
**Adopt IdGen** (NuGet, MIT), wrapped behind U1's `IIdGenerator` interface in `EventManager.Sync`.
- Configuration maps 1:1 onto Q8/D-26: `IdStructure(41, 10, 12)`, custom epoch **2026-01-01T00:00:00Z**, per-worker `generatorId`.
- **Clock-regression policy**: configure IdGen to **wait (SpinWait)** for the clock to catch up within a small bound, and **throw/alarm** beyond it — realizes the pseudocode in `business-logic-model.md §1.1`.
- IdGen owns the fiddly edge cases (sequence exhaustion per ms, backwards clock, thread safety); U1 owns the `IIdGenerator` wrapper + configuration.
- **PBT focus shifts accordingly**: BR-2.x properties test our wrapper/config and decode round-trip (monotonicity, no per-ms collision under burst, regression handling via a controllable test time source); IdGen internals are trusted as a proven dependency.
- Worker-ID assignment (`WorkerIdRegistry`, Q10) still lives in U1 and feeds IdGen's `generatorId`.

### TSD-4 — Property-based testing (inherited NFR-4.2)
**FsCheck** with xUnit integration. Domain generators for events, brackets, divisions, weights, score entries, role assignments. Shrinking on; **CI logs seeds** (PBT-08). Example-based xUnit tests pin business-critical scenarios (PBT-10).

### TSD-5 — Coverage gate (Q4=A)
**90%+ line/branch on U1** enforced in CI as a merge-blocking gate (above the 80% baseline, NFR-4.1) — justified by U1 being the correctness core.

### TSD-6 — Performance targets (Q3=A)
`NextId` ≥ ~1M ids/sec single-thread; replay/fold ≥ ~50k events < 5s; serialization round-trip sub-µs. Validated with a lightweight BenchmarkDotNet harness (optional, non-gating) during U1 build.

---

## Dependency summary (supply chain — NFR-2.9)
| Dependency | Scope | License | Notes |
|---|---|---|---|
| IdGen | runtime | MIT | Snowflake generator (TSD-3); pinned version, CI vuln scan |
| ErrorOr | runtime | MIT | result/error type for expected domain outcomes (NFR Design P-6, Q1=A) |
| System.Text.Json | runtime | BCL (MIT) | serialization (TSD-2); source-gen |
| FsCheck | test only | BSD | PBT (TSD-4) |
| xUnit | test only | Apache-2.0 | test framework |

All versions pinned; vulnerability scanning + pinned versions per NFR-2.9. Runtime dependency surface is intentionally tiny (IdGen + BCL) to keep the correctness core light and auditable.

## Traceability
- Q1→TSD-2 · Q2→TSD-3 · Q3→TSD-6 · Q4→TSD-5 · Q8/D-26→TSD-3 · NFR-4.x→TSD-4/TSD-5 · NFR-2.9→dependency summary.
