# U2 Contracts & ClientSync — Code Generation Summary

**Branch**: `unit/u2-contracts-clientsync` · **Date**: 2026-07-25 (fast-tracked)

## Created — application code
```
shared/EventManager.Contracts/
  Dtos.cs                  EventEnvelope, ReplicationBatch/Ack, Pairing req/resp, HubPushMessage, HubDiscoveryInfo, PushType
  EventEnvelopeMapper.cs   TournamentEvent <-> EventEnvelopeDto (base64 payload)
  Validators.cs            FluentValidation validators
shared/EventManager.ClientSync/
  State.cs                 ConnectionState, QueueState, SyncStatus, DeviceCredentialRef, BackoffPolicy
  Transport.cs             ISyncTransport, IHubDiscovery (seams)
  LocalEventQueue.cs       durable outbox over U1 IEventStore
  SyncClient.cs            connect + idempotent replay + status
  ReconnectSupervisor.cs   RunOnceAsync + bounded-backoff loop
  HubPushConsumer.cs       apply push via U1 ProjectionHost, typed Changed event
  PairingClient.cs         discovery + token redemption + cert pinning (ErrorOr)
shared/tests/EventManager.Contracts.Tests/    envelope round-trip + validator tests
shared/tests/EventManager.ClientSync.Tests/   fakes + durable/idempotent/reconnect/push/pairing tests
```

## Verification
- `dotnet build EventManager.Shared.slnx` → **succeeded (0/0)**.
- `dotnet test` → **42 passed / 0 failed** (Domain 20, Sync 11, Contracts 4, ClientSync 7).

## Decisions applied (fast-track, recommended)
FluentValidation (validators), System.Text.Json, typed `Changed` event for push (no System.Reactive), `ISyncTransport`/`IHubDiscovery` seams (SignalR deferred to app wiring), durability via U1 `IEventStore`, bounded backoff (1s→30s ×2).

## Coverage of BR-CS-1..8
Durable-before-ack, idempotent replay, reconnect recovery, idempotent push, single-use/empty-token pairing, bounded backoff, honest queued count — all covered by tests.

## Not applicable / deferred
API/repository/frontend/migrations/deployment — N/A (libraries). Domain REST + LAN scoring/check-in DTOs deferred to U3/U4a (Q1=A). Concrete SignalR transport + live-network integration tests deferred to app wiring / U7.
