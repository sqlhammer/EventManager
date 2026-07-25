# U4a Hub Core — Fast-Track Design (Functional + NFR + Infra, consolidated)

**Stage**: CONSTRUCTION (fast-tracked per user direction — AI-recommended decisions, no per-stage gates)
**Unit**: U4a Hub Core (`admin/`) · **Date**: 2026-07-25 · **Branch**: `unit/u4a-hub-core`

## Unit context
**Stories (7)**: US-301 (download event to hub), US-302 (hub LAN server start), US-303 (device pairing via QR), US-304 (pairing fallback manual IP), US-305 (device role management & revocation), US-407 (real-time spoke updates — transport), US-508 (mid-event device revocation).
**Nature**: Hub foundation (Critical); built before U4b. Consumes U1 (`IEventStore`, `TournamentEvent`, `RoleAuthorizationPolicy`, `WorkerIdRegistry`, `IIdGenerator`), U2 (`EventEnvelope`, pairing/push DTOs, `ISyncTransport`/`IHubDiscovery` seams).

## Key fast-track decisions (AI-recommended)
1. **Packaging (MAUI absent)**: build the hub **core as an ASP.NET Core library + host** (`admin/EventManager.Hub` + `admin/EventManager.Hub.Host`). The **MAUI Admin UI shell is deferred** as a documented seam — Hub Core is server/domain logic (embedded Kestrel, pairing, device mgmt, projections, offline auth), independent of UI. Consistent with U2's seam approach.
2. **Hub event store**: `SqliteEventStore : IEventStore` (EF Core SQLite) — mirrors U3's provider-agnostic pattern. **SQLCipher at-rest encryption is a deferred seam** (D-09; plain SQLite for MVP build).
3. **Pairing**: `PairingService` issues one-time tokens (QR payload = hub address + cert fingerprint + token + role); redemption is **single-use** (US-303), assigns a `DeviceCredential` + Snowflake worker id via U1 `WorkerIdRegistry`; manual-IP fallback (US-304) uses the same token path.
4. **Transport**: server side of `ISyncTransport`. **Concrete SignalR/WSS + mDNS are seams** (`IHubPush`, `IMdnsAdvertiser`) with a minimal in-process/no-op default; real adapters land with the MAUI host. Sync intake (`ReplicationBatchDto` from spokes) applied idempotently to the hub store.
5. **Offline auth + RBAC**: `OfflineOrganizerAuth` validates organizer credentials packaged at event download; hub-side authorization reuses the **same U1 `RoleAuthorizationPolicy`** (D-27) — authz identical to cloud.
6. **Device management**: `DeviceRegistry` — list/reassign/revoke; revocation frees the worker id and rejects the credential on next contact (US-305/508), emitted as events.
7. **Projections + health**: `HubProjectionHost` (RebuildAsync on startup + Dispatch); `/health` connected-device status (US-302, NFR-3.7).
8. **Testing**: xUnit + FsCheck on SQLite in-memory (reuse U3 harness pattern). Properties: pairing token single-use; revoked credential always rejected; worker-id uniqueness; idempotent sync intake.

## Extension compliance (fast-track)
- **Security Baseline**: WSS cert-pinning + one-time tokens + role-scoped credentials (NFR-2.1) modeled; SQLCipher deferred seam (D-09). Deny-by-default hub RBAC via U1 policy.
- **PBT**: 4 hub properties defined above.
- **Resiliency**: hub is Critical; local store durable-before-ack; recovery/backup is U7's remit.

## Deferred seams (documented, not built)
MAUI Admin UI shell; concrete SignalR/WSS transport; concrete mDNS (Makaretu); SQLCipher at-rest; hub→cloud replication client (U7 owns S-7 client side); backup/recovery (U8... U7).

## Story → component
US-301→`EventDownloadService` (readiness gate) · US-302→`HubServerHost` + `/health` · US-303→`PairingService` (QR/token) · US-304→`PairingService` (manual IP) · US-305/508→`DeviceRegistry` (revoke) · US-407→`IHubPush` seam + `SyncIntakeService`.
