# U2 Contracts & ClientSync — NFR Requirements

**Stage**: CONSTRUCTION - U2 - NFR Requirements (fast-tracked, AI-recommended)
**Branch**: `unit/u2-contracts-clientsync`
**Date**: 2026-07-25

Inherited: C#/.NET 10, FsCheck PBT, append-only/no-loss (NFR-1.1), pure library (no infra). ClientSync is part of the "core sync logic" (NFR-4.1).

| ID | Requirement |
|---|---|
| U2-REL-1 | Durable-before-ack: an accepted event is in the local store before enqueue returns (BR-CS-1) |
| U2-REL-2 | Idempotent replay end-to-end (BR-CS-2); reconnect always resyncs without user action (BR-CS-3) |
| U2-REL-3 | Honest sync status: `queuedCount` == non-acked local items at all times (BR-CS-7) |
| U2-SEC-1 | Pairing token single-use; cert fingerprint pinned and enforced on every connection (BR-CS-5/6; NFR-2.1) |
| U2-SEC-2 | All inbound DTOs validated before the event-log write path (NFR-2.4) |
| U2-PERF-1 | Envelope map + validate sub-millisecond per event; batch build O(n) |
| U2-TEST-1 | FsCheck properties for BR-CS-1..8 + validator tests; **90% coverage on ClientSync core** |
| U2-MAINT-1 | Transport is an interface seam (`ISyncTransport`/`IHubDiscovery`) so ClientSync is unit-testable with fakes; concrete SignalR/WSS impl provided at app wiring |
| U2-CONC-1 | Queue + sync status are thread-safe (background reconnect loop vs UI reads); status exposed as immutable snapshots |

Out of scope: concrete SignalR transport, live network I/O (belongs to app wiring / integration tests); domain REST DTOs (grow with U3/U4a).
