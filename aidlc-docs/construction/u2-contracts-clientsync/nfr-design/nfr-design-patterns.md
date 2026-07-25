# U2 — NFR Design Patterns (fast-tracked)

**Branch**: `unit/u2-contracts-clientsync` · **Date**: 2026-07-25

| Pattern | Realizes |
|---|---|
| **Transport seam (Ports & Adapters)** — `ISyncTransport`, `IHubDiscovery` interfaces; fakes in tests, SignalR/WSS adapter at app wiring | U2-MAINT-1, U2-TSD-4 |
| **Durable-outbox queue** — `LocalEventQueue` writes to U1 `IEventStore` before ack; per-device high-water-mark ack/prune | U2-REL-1/3, BR-CS-1/7 |
| **Idempotent replay** — batch send; hub dedupes on EventId; MarkAcked advances HWM so re-replay sends nothing new | U2-REL-2, BR-CS-2 |
| **Bounded exponential backoff** — `ReconnectSupervisor` (1s→30s, ×2); `RunOnceAsync` is the testable step | U2-TSD-7, BR-CS-3 |
| **Observer (typed event)** — `HubPushConsumer.Changed` event; pushes applied through U1 `ProjectionHost` (idempotent) | U2-TSD-3, BR-CS-4 |
| **Validation Strategy (FluentValidation)** — one validator per inbound DTO, run at the boundary | U2-SEC-2, BR contracts |
| **Cert pinning + single-use token** — enforced in `PairingClient`/transport contract | U2-SEC-1, BR-CS-5/6 |
| **Thread-safe status snapshot** — immutable `SyncStatus`; queue guarded by a lock | U2-CONC-1 |

Infra patterns (circuit breaker/cache/etc.): **N/A** for libraries — the real transport's resilience wrapping is at app wiring / U7 integration.
