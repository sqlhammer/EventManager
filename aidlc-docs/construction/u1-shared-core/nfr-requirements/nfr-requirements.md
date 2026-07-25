# U1 Shared Core — NFR Requirements

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Requirements
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Scope**: U1 is a pure library (`EventManager.Domain` + `EventManager.Sync`) — no deployment, network, or persistence engine of its own. Its NFRs are correctness, performance, and testability; storage/transport NFRs belong to consuming units (U3/U4a/U7).

---

## Performance (traces NFR-5.x)
| ID | Requirement | Target |
|---|---|---|
| U1-PERF-1 | ID generation throughput | `NextId` ≥ ~1M ids/sec single-thread (via IdGen) |
| U1-PERF-2 | Replay/fold throughput | ≥ ~50k events folded to projections in < 5s (supports hub cold-start < 30s, NFR-5.3) |
| U1-PERF-3 | Serialization round-trip | sub-microsecond per event (System.Text.Json source-gen) |
| U1-PERF-4 | Allocation discipline | replay path avoids per-event heap churn where reasonable (spans/pooling optional post-MVP) |

## Reliability & correctness (traces NFR-1.x)
| ID | Requirement |
|---|---|
| U1-REL-1 | Idempotent append + replay: `Append(Append(e))==Append(e)`, `apply(apply(log))==apply(log)` (BR-1.2/1.3) |
| U1-REL-2 | Deterministic projections: fold by ascending `EventId` yields identical state regardless of arrival order (BR-1.4) |
| U1-REL-3 | Gap-free per-device sequence tracking for replication high-water marks (BR-1.5) |
| U1-REL-4 | Lossless serialization + forward-compatible upcasting across schema versions (BR-1.6/1.7) |
| U1-REL-5 | Snowflake monotonic per worker; no collision per ms/worker; clock-regression safe (BR-2.x) |

## Security (traces NFR-2.x)
| ID | Requirement |
|---|---|
| U1-SEC-1 | Append-only, auditable event model — no mutation API on stored events (NFR-2.11, BR-1.1) |
| U1-SEC-2 | RBAC policy is pure and deny-by-default; identical decision used by cloud (U3) and hub (U4a) (NFR-2.5, BR-6.x) |
| U1-SEC-3 | Domain types carry no secrets; PII (names/DOB/weight) modeled as plain data — logging/redaction handled at boundaries, not in U1 |
| U1-SEC-4 | Supply chain: pinned dependency versions, vulnerability scan in CI, minimal dependency surface (NFR-2.9) |

## Testing & quality (traces NFR-4.x — PBT full enforcement)
| ID | Requirement |
|---|---|
| U1-TEST-1 | FsCheck property tests for every BR-x invariant (BR-1..BR-7) with domain generators (events, brackets, divisions, weights, score entries, role assignments) |
| U1-TEST-2 | Shrinking enabled; **seeds logged in CI** (PBT-08) for reproducibility |
| U1-TEST-3 | Example-based xUnit tests pin all business-critical scenarios alongside PBT (PBT-10) |
| U1-TEST-4 | **Coverage gate: 90%+ line/branch on U1** (above the 80% baseline, Q4=A) — blocks merge |
| U1-TEST-5 | Projection oracle test: optimized incremental projection == naive full fold |

## Maintainability & portability
| ID | Requirement |
|---|---|
| U1-MAINT-1 | Pure functions / immutable types in `Domain`; side-effect-free engines (no I/O) |
| U1-MAINT-2 | Minimal dependencies: IdGen + System.Text.Json (runtime); FsCheck + xUnit (test-only) |
| U1-MAINT-3 | Target framework consumable by MAUI clients and ASP.NET backend (net10.0; no platform-specific APIs) |
| U1-MAINT-4 | Clear `Domain` vs `Sync` package separation (Q1 app-design decision) — no cyclic references |

## Explicitly out of scope for U1
- Persistence engines (SQLite/SQLCipher, PostgreSQL) — thin adapters in U3/U4a implement `IEventStore`.
- Transport/TLS/SignalR, health checks, retry/backoff wiring — U4a/U7.
- Deployment/infra — none (library).
