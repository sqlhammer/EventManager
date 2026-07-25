# U2 Contracts & ClientSync — Business Logic Model

**Stage**: CONSTRUCTION - U2 - Functional Design
**Branch**: `unit/u2-contracts-clientsync`
**Date**: 2026-07-25
**Altitude**: technology-agnostic behavior. ClientSync is the spoke resilience library reused by Judge (U5) and Check-In (U6).

---

## 1. LocalEventQueue (durable-before-ack, Q2=A)
```
EnqueueDurableAsync(evt):
   idFrom IIdGenerator already assigned by caller
   store.AppendIfNotExistsAsync(evt)     # durable local write (U1 IEventStore) BEFORE any ack
   return                                 # only now may the UI confirm (NFR-1.1)

PendingAsync():  read local events with QueueState != Acked, in SequenceNumber order
MarkAckedAsync(perDeviceHwm): mark items <= hwm as Acked (prune-eligible)
```
Invariant: nothing is acknowledged to the user before it is durably persisted (BR-CS-1).

## 2. SyncClient (replay on connect/reconnect)
```
ConnectAsync(credential):
   open WSS to hub pinned to credential.hubCertFingerprint
   state = Connected
ReplayPendingAsync():
   batch = PendingAsync()  -> ReplicationBatchDto (EventEnvelopeDto[])
   send; receive ReplicationAckDto
   MarkAckedAsync(ack.perDeviceHighWaterMarks)     # idempotent: hub dedupes on EventId (U1)
   update SyncStatus (queuedCount, lastAckedSequence)
```
Invariant: replay is idempotent end-to-end — re-sending already-acked events changes nothing (BR-CS-2, relies on U1 `AppendIfNotExists`).

## 3. ReconnectSupervisor (auto-reconnect, US-507)
```
loop while running:
   if Disconnected:
      delay = min(maxDelay, initialDelay * multiplier^attempts)   # bounded backoff (NFR-3.8)
      wait(delay)
      try ConnectAsync -> on success: attempts=0; ReplayPendingAsync(); DrainInboundPush()
      on failure: attempts++
```
Invariant: no user action needed to resync; reconnect always followed by replay + missed-update download (BR-CS-3).

## 4. HubPushConsumer (Q3=A — typed subscription)
```
OnConnected: subscribe to hub SignalR stream
OnPush(HubPushMessageDto m):
   decode payload -> apply to local ProjectionHost (U1)
   raise Changed(pushType)          # typed notification the app subscribes to
```
Invariant: pushed updates are applied through the same idempotent projection path as replay (BR-CS-4).

## 5. PairingClient (discovery + enrollment, FR-4.3/4.4)
```
DiscoverAsync(): mDNS query -> fallback: manual IP entry / QR payload (HubDiscoveryInfoDto)
PairAsync(qrOrToken):
   validate PairingRequestDto
   open WSS pinned to discovered certFingerprint
   redeem one-time enrollmentToken -> receive PairingResponseDto (deviceId, workerId, role, certFingerprint)
   persist DeviceCredentialRef
```
Invariants: token is single-use (second use rejected, BR-CS-5); cert fingerprint is pinned at pairing and enforced on every later connection (BR-CS-6).

## 6. Contracts validation
Each inbound DTO is validated before use (rules in `business-rules.md`); envelope payloads are opaque base64 decoded to `TournamentEvent` via U1's serializer at the boundary.

---
## Cross-references
- Types: `domain-entities.md` · Rules/invariants + PBT: `business-rules.md` · Dependencies: U1 `EventManager.Sync`.
