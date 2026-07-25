# U7 Offline Resilience — Fast-Track Design

**Stage**: CONSTRUCTION (fast-tracked) · **Unit**: U7 Offline Resilience (cross-cutting, Critical)
**Date**: 2026-07-25 · **Branch**: `unit/u7-offline-resilience` · Code under CS-1 (no ternaries).

## Stories (8)
US-501 zero-internet full event · US-502 judge offline queue & replay · US-503 check-in offline queue ·
US-504 hub→cloud replication & outage replay · US-505 hub local backup export · US-506 manual hub
recovery · US-507 spoke auto-reconnect · US-602 post-event cloud completeness.

## What already exists (integrate, don't rebuild)
- **U2 ClientSync**: `LocalEventQueue` (durable outbox), `ReconnectSupervisor`, `SyncClient`, `HubPushConsumer` — the spoke offline-queue/reconnect primitives (US-502/503/507).
- **U1 Sync**: `IReplicationProtocol.NextBatchAsync`/`DetectGapsAsync`, `IEventStore`, `EventEnvelopeMapper`.
- **U3**: cloud `EventIngestController` (idempotent ingest, the replication sink).
- **U4a**: `HubEventStore`, `SyncIntakeService` (spoke→hub intake).

## What U7 adds (in `admin/EventManager.Hub/Resilience/`)
1. **`ICloudReplicationTransport`** seam + **`StoreBackedReplicationTransport`** (in-proc/loopback impl over an `IEventStore` "cloud"; the real HTTP adapter to `EventIngestController` is a deferred seam, like U4a's `IHubPush`).
2. **`ReplicationClient`** (US-504/602): drives hub→cloud replication using `IReplicationProtocol`; tracks per-device cloud high-water marks; bounded retry/backoff; resumes gap-free after an outage; **offline is a no-op that resumes on reconnect**; `CompletenessAsync` verifies every device's local HWM is mirrored (US-602).
3. **`BackupService`** (US-505): exports the hub log as an **encrypted, integrity-checked** snapshot (AES + SHA-256 over the serialized `EventEnvelopeDto` list).
4. **`RecoveryService`** (US-506): restores from a snapshot — decrypt → verify integrity → **rebuild by idempotent replay** into a target `IEventStore`.

## Key decisions (AI-recommended)
- **Idempotency is the backbone**: replication and recovery both use `AppendIfNotExists`, so replays/retries/re-runs never duplicate (the zero-loss + no-duplicate guarantees).
- **Cloud transport is a seam**: keeps U7 self-contained and testable without coupling `admin/`→`backend/`; the store-backed impl doubles as a local loopback and the test cloud.
- **Encryption**: AES-CBC with PBKDF2-derived key from a passphrase; SHA-256 integrity hash inside the encrypted envelope. (SQLCipher at-rest remains a separate deferred seam.)

## Owns the resilience integration/PBT suite
- **Zero-internet property (US-501)**: for any sequence of hub-applied events with the cloud offline the whole time, hub state is complete/independent; bringing the cloud online replicates every event exactly once (idempotent) → completeness holds.
- Outage replay + completeness (US-504/602); backup/restore round-trip (US-505/506); spoke offline queue drain via U2 `LocalEventQueue` (US-502/503); auto-reconnect resume (US-507).

## Deferred seams
Real HTTP cloud-replication adapter; concrete spoke reconnect scheduling in the MAUI hosts (U5/U6); SQLCipher; hot standby (D-02).
