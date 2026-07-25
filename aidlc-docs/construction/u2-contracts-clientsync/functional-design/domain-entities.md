# U2 Contracts & ClientSync — Domain Entities / Types

**Stage**: CONSTRUCTION - U2 - Functional Design
**Branch**: `unit/u2-contracts-clientsync`
**Date**: 2026-07-25
**Scope (Q1=A)**: transport-level contracts now; domain request/response DTOs grow with U3/U4a.

---

## A. `EventManager.Contracts` — transport-level DTOs

| DTO | Fields | Purpose |
|---|---|---|
| **EventEnvelopeDto** | `eventId, deviceId, sequenceNumber, eventType, schemaVersion, payloadBase64, occurredAt, eventScopeId` | wire form of `TournamentEvent` (U1); payload carried as base64 |
| **ReplicationBatchDto** | `events: EventEnvelopeDto[]` | hub→cloud batch (US-504) and spoke→hub replay |
| **ReplicationAckDto** | `acceptedCount, perDeviceHighWaterMarks: map<deviceId,seq>` | ack with updated cursors |
| **PairingRequestDto** | `enrollmentToken, devicePublicInfo` | spoke redeems a one-time token (FR-4.4) |
| **PairingResponseDto** | `deviceId, workerId, roleDescriptor, hubCertFingerprint` | credential + Snowflake worker id + pinned cert |
| **HubPushMessageDto** | `pushType (BracketUpdated \| ScheduleChanged \| ResultsUpdated \| DeviceRevoked), payloadBase64` | SignalR push envelope (FR-4.7) |
| **HubDiscoveryInfoDto** | `hubAddress, port, certFingerprint` | mDNS / manual-IP / QR discovery payload (FR-4.3) |

**Validation seam**: each inbound DTO has validation rules (see `business-rules.md`); the validator abstraction/library is chosen in NFR Requirements. Rules run **before** the event-log write path (NFR-2.4).

## B. `EventManager.ClientSync` — behavioral state types

| Type | Fields | Notes |
|---|---|---|
| **QueuedItem** | `TournamentEvent evt, QueueState state` | durable in the local `IEventStore` (Q2=A) |
| **QueueState** | `Pending \| Sent \| Acked` (enum) | acked items may be pruned |
| **ConnectionState** | `Disconnected \| Connecting \| Connected` (enum) | drives reconnect loop |
| **SyncStatus** | `ConnectionState connection, int queuedCount, long lastAckedSequence, DateTimeOffset? lastSyncAt` | surfaced honestly to UI (US-502/503) |
| **DeviceCredentialRef** | `deviceId, workerId, roleDescriptor, hubCertFingerprint` | from pairing; used on every connection |
| **BackoffPolicy** | `initialDelay, maxDelay, multiplier` | reconnect cadence (params set in NFR Design) |

## C. Dependencies (from U1, already merged)
- `TournamentEvent`, `IEventStore`, `IIdGenerator` (`EventManager.Sync`); durability reuses `IEventStore` (Q2=A) with the SQLite/SQLCipher adapter injected by the spoke apps (U5/U6).
- `IProjection`/`ProjectionHost` for applying pushed updates locally (Q3=A).

## Not in scope for U2 (grow later)
Registration/event/division/organizer/results REST DTOs → added to `Contracts` when U3 is built; scoring/check-in LAN DTOs → when U4a/U5/U6 are built. `Contracts` remains the single source of truth as they land.
