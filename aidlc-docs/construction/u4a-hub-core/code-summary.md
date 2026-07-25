# U4a Hub Core — Code & Verification Summary

**Stage**: CONSTRUCTION → Code Generation (fast-tracked) · **Unit**: U4a Hub Core
**Date**: 2026-07-25 · **Builds green; 5 tests passing.** Branch `unit/u4a-hub-core`.

## What shipped
`admin/EventManager.Hub` (ASP.NET Core host + hub-core logic) and `admin/tests/EventManager.Hub.Tests`. MAUI Admin UI shell **deferred** (workload absent) — Hub Core is server/domain logic.

| Area | Files | Stories |
|---|---|---|
| Persistence | `HubDbContext` (SQLite), `HubEventStore : IEventStore`, `HubEntities` (event log, device, pairing token, organizer credential, readiness) | — |
| Events/Projections | `HubEvents` (DevicePaired/Revoked/RoleChanged + `HubEventWriter`), `HubProjectionHost` (DeviceRecord) | US-305/508 |
| Services | `PairingService` (single-use token, worker-id), `DeviceRegistry` (list/revoke/role), `OfflineOrganizerAuth` (hub RBAC via U1 policy), `SyncIntakeService` (idempotent intake), `EventDownloadService` (readiness), `Seams` (`IHubPush`/`IMdnsAdvertiser`) | US-301/303/304/305/407/508 |
| API/host | `PairingController`, `SyncController`, `DeviceController`, `/health` (connected devices), `Program.cs` | US-302 |

## Consumes
U1 `IEventStore`/`IIdGenerator`/`WorkerIdRegistry`/`RoleAuthorizationPolicy`; U2 `EventEnvelope` + pairing/push DTOs.

## Tests (5 passing)
Pairing token single-use (US-303); unique worker-id assignment; revoked-device intake rejected (US-508); **idempotent intake property** (replay never duplicates); hub RBAC deny-by-default.

## Deferred seams (documented, not built)
MAUI UI shell; concrete SignalR/WSS (`IHubPush`); concrete mDNS (`IMdnsAdvertiser`, Makaretu); SQLCipher at-rest (D-09); hub→cloud replication client (U7 owns S-7 client). Design rationale in `fast-track-design.md`.

## Verify
```bash
dotnet build admin/EventManager.Admin.slnx
dotnet test  admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj   # 5 passed
dotnet run --project admin/EventManager.Hub                                     # GET /health → {status, connectedDevices}
```
Pairing walkthrough: `POST /api/pairing/tokens {eventId, roleDescriptor}` → QR payload → `POST /api/pairing/redeem {enrollmentToken}` → device credential + worker id (second redeem of the same token → 409). `DELETE /api/events/{id}/devices/{deviceId}` revokes; a subsequent `POST /api/sync/batch` from that device → 403.
