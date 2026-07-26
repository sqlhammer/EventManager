# EventManager — System Testing Guide (end-to-end)

**Scope**: the whole MVP across all 9 units — cloud backend, admin hub, and the judge/check-in spokes.
**Status**: 2026-07-25 · all units merged to `main`; 96 automated tests green. Per-unit guides are linked
at the end. Where a real UI does not yet exist (MAUI shells), the guide covers the tested app-core flow
and marks the manual UI walkthrough as pending.

---

## 0. Prerequisites
- **.NET 10 SDK** (`dotnet --version` ≥ 10.0.x).
- **Docker + Docker Compose** (for the cloud backend against PostgreSQL).
- **dotnet-ef** (migrations): `dotnet tool install --global dotnet-ef --version 10.0.0`.
- **Optional — MAUI Windows head**: `dotnet workload install maui-windows` (+ `maui-android`, and a JDK + Android SDK, for an Android head). iOS/Mac heads need macOS + Xcode.

## 1. Build & test everything (no infra needed)
```bash
cd C:\repos\EventManager
dotnet test shared/EventManager.Shared.slnx        # 42  (U1 Domain/Sync, U2 Contracts/ClientSync)
dotnet test backend/EventManager.Backend.slnx      # 26  (U8 Payments 6, U3 Api 20)
dotnet test admin/EventManager.Admin.slnx          # 17  (U4a hub 5, U4b competition 7, U7 resilience 5)
dotnet test judge/tests/EventManager.Judge.Core.Tests/EventManager.Judge.Core.Tests.csproj      # 6  (U5)
dotnet test checkin/tests/EventManager.Checkin.Core.Tests/EventManager.Checkin.Core.Tests.csproj # 5  (U6)
```
Expected: **96 passing**, 0 failing. These cover the four PBT invariants (cloud) + hub pairing/scoring/
resilience properties + spoke durable-before-ack.

Optional compiling MAUI Windows heads:
```bash
dotnet build judge/EventManager.Judge/EventManager.Judge.csproj       # Build succeeded (Windows)
dotnet build checkin/EventManager.Checkin/EventManager.Checkin.csproj  # Build succeeded (Windows)
```

---

## 2. Cloud backend walkthrough (U3 + U8) — REST, manual

Bring it up (see [U3 guide](construction/u3-cloud-backend/code/user-testing-guide.md) for detail):
```bash
cd backend && cp .env.example .env   # edit secrets
docker compose up --build            # proxy(TLS) + api + db + backup
curl -k https://localhost/health/ready   # → Healthy (deep DB probe)
```
Use `Authorization: Bearer <accessToken>` after login for every authenticated call.

| # | Step | Endpoint | Expect |
|---|---|---|---|
| 1 | Register organizer | `POST /api/accounts/register` | 200; confirmation token in the **EmailOutbox** table (email stubbed). A breached password (`password`) is rejected. |
| 2 | Confirm email | `POST /api/accounts/confirm-email` | 200 (gate for event creation) |
| 3 | Login | `POST /api/accounts/login` | `{accessToken, refreshToken, accessExpiresAt}` |
| 4 | (opt) MFA | `POST /api/accounts/mfa/enroll` → `/mfa/confirm` | QR/`otpauth` + recovery codes; later logins need `totp` |
| 5 | Create event | `POST /api/events` | `{id}`; creator auto Full Admin |
| 6 | Configure division | `POST /api/events/{id}/divisions` | `{id}`; overlapping slice → 409 |
| 7 | Weigh-in policy / payment options | `PUT .../weigh-in-policy`, `PUT .../payment-options` | 200 |
| 8 | Open registration | `POST /api/events/{id}/registration/open` | 200 |
| 9 | Athlete profile | `POST /api/registration/profiles` | `{id}` |
| 10 | Register athlete | `POST /api/registration` | fee + `paymentStatus` (Owed/Paid); same division twice → rejected |
| 11 | Coach bulk | `POST /api/registration/batch` (idempotencyKey) | all-or-nothing; conflicts itemized; resubmit = no-op |
| 12 | Roster payment | `PUT /api/registration/{id}/payment-status` | mark Paid (cash) / Waived |
| 13 | Add co-organizer | `POST /api/events/{id}/organizers` | Full-Admin only; last-admin demote refused |
| 14 | Ingest (replication sink) | `POST /api/ingest/batch` | event-scoped, idempotent |
| 15 | Results | `GET /api/results/athletes/{athleteId}` | empty until event-day events ingested |

**Negative checks**: repeated bad logins → lockout; generic (non-enumerating) errors; window-closed registration → 409; Co-Organizer doing a Full-Admin-only action → 403; >5 logins/min or >10 registrations/hr per IP → 429.

Stubbed by design: **email** (outbox), **card payments** (U8 stub, no live charge).

---

## 3. Admin hub walkthrough (U4a + U4b) — REST, manual

Run the hub host:
```bash
dotnet run --project admin/EventManager.Hub    # SQLite local store; GET /health → connected devices
```

**Pairing & devices (U4a):**
| Step | Endpoint | Expect |
|---|---|---|
| Issue pairing token (organizer) | `POST /api/pairing/tokens {eventId, roleDescriptor}` | QR payload (hub address + cert fingerprint + one-time token + role) |
| Redeem (spoke) | `POST /api/pairing/redeem {enrollmentToken}` | device credential + Snowflake worker id; **re-redeeming the same token → 409** (single-use) |
| List / revoke device | `GET`/`DELETE /api/events/{id}/devices[/{deviceId}]` | revoke frees the worker id; a later `POST /api/sync/batch` from that device (`X-Device-Id`) → **403** |

**Competition (U4b):**
| Step | Endpoint | Expect |
|---|---|---|
| Assign a device to a mat/division | `POST /api/events/{id}/competition/mat-assignments {deviceId, divisionId}` | 200 |
| Advance a match | `POST .../competition/divisions/{divisionId}/bracket:advance` | winner recorded; standings updated |
| Start a division | `POST .../bracket:start` | further **regeneration blocked** |
| Standings | `GET .../divisions/{divisionId}/standings` | ordered by wins |
| Finalize | `POST .../divisions/{divisionId}:finalize` | placements by wins |
| Dispute flag/resolve | `POST .../disputes`, `PUT .../disputes/{id}` | recorded as events |

Mat authority (US-406): a score submitted by a device **not** assigned to the match's division is rejected — see the `Foreign_mat_score_is_rejected` test. Bracket generation itself is driven by the hub from the cleared roster (`BracketService`); the HTTP surface here exposes advance/start/finalize/standings/disputes.

Detail: [U4a guide](construction/u4a-hub-core/user-testing-guide.md) · [U4b guide](construction/u4b-hub-competition/user-testing-guide.md).

---

## 4. Offline resilience (U7) — verified via tests

U7 has no manual HTTP surface of its own (it's the hub-side replication/backup/recovery driver). Verify with:
```bash
dotnet test admin/tests/EventManager.Hub.Tests/EventManager.Hub.Tests.csproj --filter "FullyQualifiedName~Resilience"
```
Covers: **outage → reconnect replicates every event exactly once** + completeness (US-501/504/602); **backup/restore round-trip** + tampered-backup integrity failure (US-505/506); the **zero-internet full-event property** (PBT, US-501); **spoke offline-queue drain** on reconnect (US-502/503). The real HTTP cloud-replication adapter is a deferred seam; the loopback transport stands in.
Detail: [U7 guide](construction/u7-offline-resilience/user-testing-guide.md).

---

## 5. Spoke apps (U5 Judge, U6 Check-In) — app-core verified; UI walkthrough pending

The MAUI heads compile (Windows) but ship as **template shells** — the interactive UI is not wired yet, so a click-through walkthrough is **pending**. The app-core logic (the testable substance) is verified:
- **U5 Judge**: score capture is **durable-before-ack**; contiguous per-device sequences; queued scores drain after hub ack; mat-queue advances; cross-mat view is read-only; focus lock/unlock.
- **U6 Check-In**: check-in durable-before-ack; weigh-in **in-range → green / out-of-range → flagged** (via U1 evaluator); non-binding staff recommendation (D-25); corrections are new events (immutable history).
```bash
dotnet test judge/tests/EventManager.Judge.Core.Tests/EventManager.Judge.Core.Tests.csproj
dotnet test checkin/tests/EventManager.Checkin.Core.Tests/EventManager.Checkin.Core.Tests.csproj
```
Detail: [U5 guide](construction/u5-judge/user-testing-guide.md) · [U6 guide](construction/u6-checkin/user-testing-guide.md).

---

## 6. Full offline-first loop (conceptual, end-to-end)
1. Organizer sets up the event in the **cloud** (§2) and the hub downloads it (readiness gate, U4a).
2. On the venue LAN the **hub** runs; judges/check-in **pair** (§3) and get mat-scoped credentials.
3. Spokes capture scores/check-ins **durably before ack** (§5) and sync to the hub idempotently; the hub advances brackets with **mat authority** (§3).
4. With the internet down, everything above keeps working (**zero-internet property**, §4).
5. On reconnect the hub **replicates** its log to the cloud **exactly once**, and post-event **completeness** is verifiable (§4). Backups/recovery guard the hub log.

## 7. Per-unit guides
- Library/foundation: [U1](construction/u1-shared-core/user-testing-guide.md) · [U2](construction/u2-contracts-clientsync/user-testing-guide.md) · [U8](construction/u8-payment-stub/user-testing-guide.md)
- Cloud: [U3](construction/u3-cloud-backend/code/user-testing-guide.md)
- Hub: [U4a](construction/u4a-hub-core/user-testing-guide.md) · [U4b](construction/u4b-hub-competition/user-testing-guide.md)
- Resilience: [U7](construction/u7-offline-resilience/user-testing-guide.md)
- Spokes: [U5](construction/u5-judge/user-testing-guide.md) · [U6](construction/u6-checkin/user-testing-guide.md)
