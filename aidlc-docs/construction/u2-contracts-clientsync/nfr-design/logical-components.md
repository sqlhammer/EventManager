# U2 — Logical Components (fast-tracked)

**Branch**: `unit/u2-contracts-clientsync` · **Date**: 2026-07-25

## Contracts (`EventManager.Contracts`)
| Component | Role |
|---|---|
| DTO records | EventEnvelope, ReplicationBatch/Ack, Pairing req/resp, HubPushMessage, HubDiscoveryInfo |
| `EventEnvelopeMapper` | TournamentEvent ⇄ EventEnvelopeDto (base64 payload) |
| FluentValidation validators | one per inbound DTO (U2-TSD-1) |

## ClientSync (`EventManager.ClientSync`)
| Component | Role | Owner of impl |
|---|---|---|
| `ISyncTransport` | connect / send batch / subscribe push / redeem pairing | interface here; SignalR adapter at app wiring |
| `IHubDiscovery` | mDNS + manual/QR discovery | interface here |
| `LocalEventQueue` | durable outbox over U1 `IEventStore` | here |
| `SyncClient` | connect + idempotent replay + status | here |
| `ReconnectSupervisor` | bounded backoff loop; `RunOnceAsync` step | here |
| `HubPushConsumer<TState>` | apply push to U1 `ProjectionHost`; raise `Changed` | here |
| `PairingClient` | discovery + token redemption + cert pinning | here |
| `SyncStatus` / `ConnectionState` / `BackoffPolicy` / `DeviceCredentialRef` | state | here |

Infra components: **N/A** (library). Concrete transport + persistence adapters are provided by consuming apps (U5/U6) and integration-tested (U7).
