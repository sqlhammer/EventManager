# Integration Test Instructions

**Purpose**: verify the interactions *between* units — the seams that unit tests deliberately stub.

**Status, stated plainly**: there is **no automated integration test suite**. Every scenario below is
a manual procedure. NFR-4.4 makes integration tests for LAN disconnect/reconnect-replay a
**post-MVP recommendation, not a required CI gate**, so this matches the approved plan rather than
falling short of it. The gaps are listed at the end.

---

## What is already covered by unit tests

Several cross-unit interactions are exercised in-process because the components are
provider-agnostic and compose without infrastructure:

| Interaction | Where | Why it counts |
|---|---|---|
| U3 API → U1 event store → U1 projections | `EventManager.Api.Tests` | Real `PostgresEventStore` logic on SQLite; projections fold inline in the same transaction |
| U3 ingest ← U1 replication protocol | `IngestServiceTests` | Idempotency under any order/partition/repetition (PBT-4) |
| U4b competition → U1 engines | `EventManager.Hub.Tests` | Bracket/scoring/weigh-in orchestration against the real engines |
| U7 replication → U1 protocol + U2 queue | `EventManager.Hub.Tests` | Outage replay and completeness, zero-internet property |
| U3 API → U8 payments | `RegistrationServiceTests` | Decline path maps to `Owed` |
| U9 read tiers → U3 read models | `ReadTierTests`, `ReadShapeTests` | Tier resolution over projection output |

What they do **not** cover is anything crossing a **process or network boundary**: HTTP between
hub and cloud, TLS termination at Caddy, real PostgreSQL, and device pairing over a LAN.

---

## Scenario 1 — Cloud backend against real PostgreSQL

**Tests**: U3 + U8 + U9 against the real database and proxy, rather than SQLite.

**Setup**
```bash
cd backend
cp .env.example .env      # set POSTGRES_*, JWT_SIGNING_KEY, BACKUP_ENCRYPTION_KEY
docker compose up -d --build
curl -k https://localhost/health/ready     # deep DB probe must report healthy
```

**Steps**: run the Postman collection folder **Cloud Backend (U3 + U8 + U9)** in order — Accounts &
Auth (1–4) → Events & Divisions (5–8) → Registration (9–12) → Organizers (13) → Read API (16–25).

**Expected**
- Migrations apply at startup (Development only), so the schema exists on first run
- Every request returns its documented status; the Read API folder's assertions pass
- The three NEG requests return **404, never 403**

**Cleanup**: `docker compose down` (add `-v` to drop the database volume).

---

## Scenario 2 — Hub → cloud replication over HTTP

**Tests**: U7 `ReplicationClient` → U3 `EventIngestController`, the system's most important seam.

⛔ **Blocked.** `ICloudReplicationTransport` has only `StoreBackedReplicationTransport`, an
in-process implementation. The **real HTTP adapter does not exist** — it is an open deferred seam
(`admin/EventManager.Hub/Resilience/CloudReplicationTransport.cs`). Until it is built, hub→cloud
replication cannot be integration-tested over a network; it is covered in-process only.

Both ends are ready — the hub client and the cloud endpoint exist and `ReplicationBatchDto` is
shared via U2 — so this is a buildable unit of work, not a design gap.

---

## Scenario 3 — Spoke → hub pairing and sync over the LAN

**Tests**: U5/U6 spokes → U4a hub pairing, device registry, sync intake.

**Setup**
```bash
dotnet run --project admin/EventManager.Hub      # hub host on http://localhost:5000
```

**Steps**: use the Postman folder **Admin Hub (U4a + U4b)** — issue a pairing token as organizer,
redeem it as a spoke, list devices, post a sync batch, then assign the device to a mat and run the
competition requests.

**Expected**: single-use token redemption; a revoked device is refused; a resubmitted sync batch is
idempotent (no duplicate events).

**Not covered**: real mDNS discovery and SignalR push are no-op seams
(`admin/EventManager.Hub/Services/Seams.cs`), so devices are addressed by explicit URL rather than
discovered. Genuine LAN discovery is untested.

---

## Scenario 4 — Offline-first loop (the flagship behaviour)

**Tests**: NFR-1.1 zero data loss and NFR-1.2 indefinite offline operation, end to end.

**Currently verified by test, not by a live rig** — `EventManager.Hub.Tests` covers outage replay,
completeness verification, backup/restore with integrity checking, and a zero-internet full-event
property.

A live rehearsal would be: start hub + spokes on an isolated LAN with no internet, run a full event
(check-in → weigh-in → bracket → scoring → finalize), reconnect, and assert the cloud holds 100% of
the event log via `VerifyCompletenessAsync`. That rehearsal **depends on Scenario 2**, so it is
blocked on the same missing HTTP adapter.

---

## Scenario 5 — Backup and restore

**Setup**: the `backup` service runs in Compose against the same database.

**Steps**: trigger `backend/scripts/backup.sh`, then restore into a clean volume and re-run the
Scenario 1 read requests.

**Expected**: restored data matches; a tampered backup fails its SHA-256 integrity check (already
asserted in `EventManager.Hub.Tests` for the hub-side implementation).

---

## Known gaps

| Gap | Consequence | Blocked on |
|---|---|---|
| No automated integration suite | All scenarios are manual | Post-MVP by decision (NFR-4.4) |
| No HTTP replication adapter | Scenarios 2 and 4 cannot run live | A unit of work — both ends already exist |
| mDNS and SignalR are no-ops | LAN discovery and push untested | Concrete adapters + MAUI host |
| MAUI heads are template shells | No UI-level e2e is possible | Real UI implementation |
| SMTP is an outbox stub | Email flows verified by reading the outbox table, not by delivery | Provider choice |
| Payments are stubbed | No real charge path | Provider choice |
