# U7 Offline Resilience — Code & Verification Summary

**Stage**: CONSTRUCTION → Code Generation (fast-tracked) · **Unit**: U7 Offline Resilience
**Date**: 2026-07-25 · **Builds green; 17 hub tests passing (5 U4a + 7 U4b + 5 U7).** Branch `unit/u7-offline-resilience`. Code under CS-1 (no ternaries).

## What shipped (`admin/EventManager.Hub/Resilience/`)
| Component | Responsibility | Stories |
|---|---|---|
| `ICloudReplicationTransport` + `StoreBackedReplicationTransport` | Hub→cloud transport seam; in-proc/loopback impl over an `IEventStore` (the test cloud + local loopback). Real HTTP adapter deferred. | US-504 |
| `ReplicationClient` | Drives replication via U1 `IReplicationProtocol`; per-device cloud high-water marks; bounded retry/backoff; offline = no-op that resumes gap-free; `VerifyCompletenessAsync` | US-504/602 |
| `BackupService` | Encrypted (AES-CBC + PBKDF2), SHA-256 integrity-checked snapshot of the hub log | US-505 |
| `RecoveryService` | Decrypt → verify integrity → rebuild by **idempotent replay** into a target store | US-506 |
| `BackupCrypto` | AES helper (salt‖iv‖ciphertext) | — |

## Integrates (does not rebuild)
U1 `IReplicationProtocol`/`IEventStore`/`EventEnvelopeMapper`; U2 `LocalEventQueue` (spoke offline outbox); U3 cloud ingest (the real replication sink, behind the transport seam); U4a `HubEventStore`.

## Tests (5 U7)
- **Outage → reconnect replicates all exactly once** + completeness (US-501/504/602): offline replication is a no-op; on reconnect all events mirror; re-run idempotent; completeness true.
- **Backup/restore round-trip** (US-505/506) and **tampered-backup integrity failure**.
- **Zero-internet full-event property** (PBT, US-501): for any event count, hub is complete/independent while offline, and every event mirrors exactly once on reconnect.
- **Spoke offline queue drains on reconnect** (US-502/503) via U2 `LocalEventQueue`.

## Idempotency backbone
Replication and recovery both use `AppendIfNotExists`, so retries / re-runs / outage replays never duplicate — the zero-loss + no-duplicate guarantees underpinning US-501.

## Deferred seams
Real HTTP cloud-replication adapter; spoke reconnect scheduling in the MAUI hosts (U5/U6); SQLCipher at-rest; hot standby (D-02).

## Verify
```bash
dotnet build admin/EventManager.Admin.slnx
dotnet test  admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj   # 17 passed
```
