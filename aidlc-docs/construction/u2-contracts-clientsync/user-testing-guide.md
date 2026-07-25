# U2 Contracts & ClientSync — Testing & Verification Guide

**Unit**: U2 (`EventManager.Contracts` + `EventManager.ClientSync`) — libraries. Developer/consumer verification guide (end-of-unit deliverable).
**Branch**: `unit/u2-contracts-clientsync`

## Build & test
```bash
dotnet build shared/EventManager.Shared.slnx
dotnet test  shared/EventManager.Shared.slnx
```
Expected: build 0/0; **42 tests pass** (Domain 20, Sync 11, Contracts 4, ClientSync 7).

## What U2's tests prove
| Area | Test | Invariant |
|---|---|---|
| Contracts | `ContractsTests` | envelope round-trips losslessly; validators accept/reject correctly (worker-id range, base64) |
| Queue | `ClientSyncTests.Queue_*` | durable-before-ack; honest queued count (BR-CS-1/7) |
| Replay | `Replay_IsIdempotent` | second replay sends nothing new; queue drains (BR-CS-2) |
| Reconnect | `Reconnect_RecoversFromFailure` | fail→recover without throwing (BR-CS-3) |
| Push | `Push_IsIdempotent` | duplicate push applied once; both notifications raised (BR-CS-4) |
| Pairing | `Pairing_*` | empty token rejected; credential pins hub fingerprint (BR-CS-5/6) |
| Backoff | `Backoff_IsBounded` | delay never exceeds max (NFR-3.8) |

## How a consumer (U5/U6) wires ClientSync
1. Provide a concrete `ISyncTransport` (SignalR/WSS over the pinned cert) and `IHubDiscovery` (mDNS + manual/QR).
2. Provide a SQLite/SQLCipher `IEventStore` (U1 seam) to `LocalEventQueue`.
3. Pair once via `PairingClient.PairAsync`; then run `ReconnectSupervisor.RunLoopAsync` in the background.
4. Subscribe to `HubPushConsumer.Changed` to refresh the UI; read `SyncClient.Status` for the sync indicator.

Concrete transport + live-network behavior are integration-tested at app wiring / U7, not here.
