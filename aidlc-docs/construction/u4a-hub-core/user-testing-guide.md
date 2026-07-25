# U4a Hub Core — Developer Verification Guide

**Unit**: U4a Hub Core (`admin/EventManager.Hub`) · **Date**: 2026-07-25
Hub Core is server/library logic (the MAUI Admin UI shell is deferred), so this is a developer
verification guide. See also [system testing guide](../../testing-guide.md) §3.

## Build & test
```bash
dotnet build admin/EventManager.Admin.slnx
dotnet test  admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj --filter "FullyQualifiedName~HubCore"
```
Covers: pairing token **single-use** (US-303); **unique worker-id** assignment; **revoked-device intake rejected** (US-508); **idempotent intake** property; hub RBAC **deny-by-default**.

## Run the hub
```bash
dotnet run --project admin/EventManager.Hub
curl http://localhost:5xxx/health        # → { status: Healthy, connectedDevices: N }
```

## Pairing & device walkthrough (curl)
1. **Issue a token** (organizer): `POST /api/pairing/tokens {"eventId":1,"roleDescriptor":"Judge — Mat 2"}` → QR payload (hub address + cert fingerprint + one-time token + role).
2. **Redeem** (spoke): `POST /api/pairing/redeem {"enrollmentToken":"<token>","devicePublicInfo":"spoke"}` → `{deviceId, workerId, roleDescriptor, hubCertFingerprint}`.
3. **Re-redeem the same token** → **409** (single-use, US-303).
4. **List devices**: `GET /api/events/1/devices`.
5. **Revoke**: `DELETE /api/events/1/devices/{deviceId}` → frees the worker id; a subsequent `POST /api/sync/batch` with header `X-Device-Id: {deviceId}` → **403** (US-508).

## What is stubbed / deferred
MAUI Admin UI shell; concrete SignalR/WSS push (`IHubPush`); concrete mDNS (`IMdnsAdvertiser`); SQLCipher at-rest; hub→cloud replication client (owned by U7). Offline organizer auth + hub-side RBAC reuse the **same U1 `RoleAuthorizationPolicy`** as the cloud.
