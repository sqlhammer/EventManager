# U2 Contracts & ClientSync — Business Rules & PBT Invariants

**Stage**: CONSTRUCTION - U2 - Functional Design
**Branch**: `unit/u2-contracts-clientsync`
**Date**: 2026-07-25

---

## ClientSync invariants (PBT candidates)
| Rule | Statement | PBT invariant |
|---|---|---|
| BR-CS-1 | Durable before ack | For any accepted event, it exists in the local store before the enqueue call returns (no-loss, NFR-1.1) |
| BR-CS-2 | Idempotent replay | Replaying a pending batch any number of times yields the same hub/local state (dedupe on EventId, U1) |
| BR-CS-3 | Reconnect resyncs without user action | After Disconnected→Connected, PendingAsync() drains and missed pushes are applied |
| BR-CS-4 | Push applied idempotently | Applying the same push message twice = applying once (projection dedupe) |
| BR-CS-5 | Pairing token single-use | Redeeming an already-used enrollment token is rejected |
| BR-CS-6 | Cert fingerprint pinned | A connection whose server cert fingerprint ≠ paired fingerprint is refused |
| BR-CS-7 | Honest sync status | `SyncStatus.queuedCount` equals count of non-Acked local items at all times |
| BR-CS-8 | Sequence ordering preserved | Pending items are sent in ascending SequenceNumber per device |

## Contracts validation rules
| DTO | Rules |
|---|---|
| EventEnvelopeDto | eventId/deviceId/sequenceNumber > 0; eventType non-empty; schemaVersion ≥ 1; payloadBase64 valid base64; occurredAt not default |
| ReplicationBatchDto | events non-null; each valid; batch size ≤ configured max |
| PairingRequestDto | enrollmentToken non-empty & well-formed |
| PairingResponseDto | deviceId > 0; workerId in 0..1023; certFingerprint non-empty |
| HubPushMessageDto | pushType in known set; payloadBase64 valid |
| HubDiscoveryInfoDto | hubAddress parseable; port in 1..65535; certFingerprint non-empty |

All validation runs before the event-log write path (NFR-2.4). Invalid inbound data is rejected with a validation error (no partial application).

## Extension touchpoints
- **PBT**: BR-CS-1..8 are property targets (durable-before-ack, idempotent replay, ordering, status honesty). Generators: event streams, disordered/duplicated batches, connection drop/restore sequences.
- **Security**: BR-CS-5/6 (single-use token, cert pinning) realize NFR-2.1 at the client; validation (BR contracts) realizes NFR-2.4.
- **Resiliency**: BR-CS-3 + bounded backoff realize NFR-3.8 / US-507.

## Coverage / scope
- Frontend components: **N/A** (libraries).
- Domain request/response DTOs deferred to consumers (Q1=A) — not a gap.
