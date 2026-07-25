# U7 Offline Resilience — Developer Verification Guide

**Unit**: U7 Offline Resilience (`admin/EventManager.Hub/Resilience`) · **Date**: 2026-07-25
Cross-cutting driver logic (no standalone HTTP surface); verified via the resilience test suite.
See also [system testing guide](../../testing-guide.md) §4.

## Build & test
```bash
dotnet test admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj --filter "FullyQualifiedName~Resilience"
```
Covers (5 tests):
- **Outage → reconnect replicates all exactly once** + completeness (US-501/504/602): while the cloud transport is offline, `ReplicateAsync` is a no-op and the cloud stays empty; on reconnect every event mirrors; a re-run replicates nothing; `VerifyCompletenessAsync` reports complete.
- **Backup/restore round-trip** (US-505/506): `BackupService.ExportAsync` → `RecoveryService.RestoreAsync` rebuilds the log by idempotent replay.
- **Tampered-backup integrity failure**: flipping a ciphertext byte makes restore throw (AES + SHA-256).
- **Zero-internet full-event property** (PBT, US-501): for any event count, the hub is complete/independent offline and every event mirrors exactly once on reconnect.
- **Spoke offline-queue drain** (US-502/503): U2 `LocalEventQueue` holds events while offline and drains after the hub acks.

## How to exercise in code
`ReplicationClient(localStore, new ReplicationProtocol(), transport)` — set `transport.IsOnline=false` to simulate an outage, `true` to reconnect; call `ReplicateAsync()` then `VerifyCompletenessAsync()`. `BackupService`/`RecoveryService` take a passphrase and an `IEventStore`.

## Deferred
The real HTTP cloud-replication adapter (POST to the U3 `EventIngestController`) is a seam — the in-proc `StoreBackedReplicationTransport` stands in as the local loopback + test cloud. Spoke reconnect scheduling lands in the MAUI hosts (U5/U6). SQLCipher at-rest and hot standby are out of MVP scope.
