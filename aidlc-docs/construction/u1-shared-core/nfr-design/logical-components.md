# U1 Shared Core — Logical Components

**Stage**: CONSTRUCTION - U1 Shared Core - NFR Design
**Branch**: `unit/u1-shared-core`
**Date**: 2026-07-24
**Purpose**: The logical seams/abstractions U1 exposes, and who implements each. U1 defines interfaces + pure implementations; storage/transport adapters are implemented by consuming units.

---

## `EventManager.Sync` seams
| Abstraction | Defined in | Implemented by | Notes |
|---|---|---|---|
| `IIdGenerator` | U1 | U1 (IdGen-backed adapter, P-2) | thread-safe |
| `IWorkerIdRegistry` | U1 | U1 (registry) | assignment orchestrated at pairing/download by U4a |
| `IEventSerializer` | U1 | U1 (System.Text.Json source-gen, P-1) | MessagePack impl deferred |
| `IEventStore` | U1 | **U3** (Npgsql adapter), **U4a** (SQLite/SQLCipher adapter) | single-writer contract (P-7) |
| `IReplayEngine` | U1 | U1 | idempotent fold (P-3) |
| `IProjection<TState>` / `IProjectionHost` | U1 | U1 framework; concrete projections in consuming units | EventId-ordered fold (P-4) |
| `IReplicationProtocol` | U1 | U1 (protocol logic) | wired/tested cross-cutting by **U7** (P-10) |
| upcaster registry | U1 | U1 | `(EventType, SchemaVersion)` → upcaster (P-5) |

## `EventManager.Domain` seams
| Abstraction | Defined in | Implemented by | Notes |
|---|---|---|---|
| `IBracketEngine` | U1 | U1 | single-elim + round-robin (BR-3) |
| `ISeedingEngine` | U1 | U1 | academy separation (BR-3.6) |
| `IScoringEngine` / `IRuleset` | U1 | U1 | point-sparring + forms; pluggable rulesets (BR-4) |
| `IWeighInPolicyEvaluator` | U1 | U1 | tolerance/strict/auto-move (BR-5) |
| `IRoleAuthorizationPolicy` | U1 | U1 | pure RBAC (BR-6), shared by U3 + U4a |

## Error model
| Element | Choice |
|---|---|
| Result type | `ErrorOr<T>` (ErrorOr library, P-6) for expected domain outcomes |
| Exceptions | reserved for programmer/exceptional errors (e.g., `ClockRegressionError` beyond bound) |

## Consumer implementation map (who provides adapters)
```
U1 defines:   IEventStore, IEventSerializer(+impl), IIdGenerator(+impl), IReplayEngine(+impl),
              IProjectionHost(+framework), IReplicationProtocol(+logic), IWorkerIdRegistry(+impl),
              domain engines (+impl), IRoleAuthorizationPolicy (+impl)
U3 provides:  IEventStore -> PostgreSQL (Npgsql)
U4a provides: IEventStore -> SQLite/SQLCipher; drives IWorkerIdRegistry at pairing
U7 provides:  cross-cutting wiring + tests for IReplicationProtocol, backup/recovery, spoke queue integration
```

## Infrastructure components — N/A
U1 has **no** queues, caches, circuit breakers, connection pools, schedulers, or health endpoints — it is a pure library. Such components live in U3/U4a/U7. Documented here to satisfy the NFR-Design component review with explicit N/A rationale.
