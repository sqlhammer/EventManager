# U3 Cloud Backend — Developer Verification Guide

**Unit**: U3 Cloud Backend · **Date**: 2026-07-25
U3 is a headless API (no UI), so this is a developer verification walkthrough: how to build, test, run, and exercise the endpoints end-to-end.

---

## 1. Prerequisites
- .NET 10 SDK (`dotnet --version` ≥ 10.0.x).
- Docker + Docker Compose (for the full run against PostgreSQL).
- `dotnet-ef` global tool (for migrations): `dotnet tool install --global dotnet-ef --version 10.0.0`.

## 2. Build & test (no database needed)
```bash
dotnet build backend/EventManager.Backend.slnx
dotnet test  backend/tests/EventManager.Api.Tests/EventManager.Api.Tests.csproj
```
Expected: **Build succeeded**; **20 passed** (SQLite in-memory — no PostgreSQL required). The suite covers the four PBT invariants plus example scenarios.

## 3. Run the full stack (Docker Compose)
```bash
cd backend
cp .env.example .env          # then edit secrets (JWT key, DB password, backup key)
docker compose up --build
```
Services: `proxy` (Caddy TLS on :443) → `api` (:8080 internal) → `db` (PostgreSQL) + `backup` sidecar. Migrations apply automatically in Development; for Production run `dotnet ef database update` (or the container entrypoint) once.

`docker-compose.override.yml` (committed, dev-only — merges automatically, no extra flags) publishes `db` on `localhost:5432` so you can connect an IDE/GUI client (DBeaver, pgAdmin, etc.) directly using the credentials in your `.env`. Prod does not expose this port — its deploy commands pin `-f docker-compose.yml` explicitly (see deployment-architecture.md §3/4).

Health checks:
```bash
curl -k https://localhost/health          # liveness → Healthy
curl -k https://localhost/health/ready     # deep DB probe → Healthy
```

## 4. Run locally without containers (dev)
Point `ConnectionStrings:Postgres` at a local PostgreSQL (see `appsettings.Development.json`), then:
```bash
cd backend/EventManager.Api
export ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

## 5. End-to-end walkthrough (happy path)
Base URL below assumes the proxy; adjust for local `dotnet run` (http://localhost:5xxx).

1. **Register an organizer** — `POST /api/accounts/register` `{ "email": "org@dojo.com", "password": "a-strong-unbreached-passphrase" }`
   → 200. A confirmation token is written to the **EmailOutbox** table (email is stubbed, Q5). A breached password (e.g. `password`) is rejected (BR-AUTH-1).
2. **Confirm email** — read the token from the outbox, `POST /api/accounts/confirm-email` `{ "email": "...", "token": "..." }`. (Confirmation gates event creation, BR-AUTH-4.)
3. **Login** — `POST /api/accounts/login` → `{ accessToken, refreshToken, accessExpiresAt }`. Use `Authorization: Bearer <accessToken>` for the rest.
4. **Create event** — `POST /api/events` (creator becomes Full Admin automatically, D-20) → `{ id }`.
5. **Open registration** — `POST /api/events/{eventId}/registration/open`.
6. **Configure a division** — `POST /api/events/{eventId}/divisions` (overlapping divisions in the same slice are rejected, BR-DIV-1) → `{ id }`.
7. **Create an athlete profile** — `POST /api/registration/profiles`.
8. **Register the athlete** — `POST /api/registration` `{ eventId, athleteId, divisionIds:[...], payByCard:false }` → confirmation + fee + `paymentStatus:"Owed"`. Registering the same division again is rejected (BR-REG-5).
9. **Coach bulk** — `POST /api/registration/batch` with an `idempotencyKey`; any conflict itemizes and commits nothing (Q2=A); resubmitting the same key is a no-op.
10. **Roster payment** — `PUT /api/registration/{registrationId}/payment-status` `{ "status": "Paid" }` (organizer marks cash paid, BR-PAY-2).
11. **Add a co-organizer** — `POST /api/events/{eventId}/organizers` `{ "accountId": <id> }` (Full-Admin only). Demoting the last Full Admin is refused (BR-RBAC-3).
12. **Ingest (replication)** — `POST /api/ingest/batch` with a `ReplicationBatchDto`; event-scoped-authorized, idempotent; replays never duplicate (US-504).
13. **Results** — `GET /api/results/athletes/{athleteId}` → results/history (empty until real event-day events are ingested, Q6=A).

## 6. Negative paths worth checking
- Login with a wrong password repeatedly → progressive lockout (BR-AUTH-2); responses are generic/non-enumerating (BR-AUTH-3).
- Card path with a forced decline (via a real provider later) → registration stays `Owed` with a retry path (BR-PAY-3).
- Registration when the window is closed → 409 (BR-REG-4).
- A Co-Organizer attempting a Full-Admin-only action → 403 (BR-RBAC-2).
- Rate limits: >5 logins/min or >10 registrations/hr from one IP → 429.

## 7. What is stubbed (by design)
- **Email** — recorded to the outbox, not sent (Q5, D-06 pattern).
- **Card payments** — the U8 `StubPaymentProvider`; no live charges (D-06).
- Real SMTP, live payment provider + circuit breaker, column-level encryption, async projector, and multi-instance scale-out are documented seams, not built.

## 8. MFA (optional)
`POST /api/accounts/mfa/enroll` (authenticated) → shared key + `otpauth://` URI + recovery codes; scan into an authenticator, then `POST /api/accounts/mfa/confirm` `{ "totp": "123456" }`. Subsequent logins then require the `totp` field.

## 9. Delete your account (US-110)
`DELETE /api/accounts/me` (authenticated) `{ "password": "<current password>", "totp": "123456" }` — `totp` only when MFA is enrolled. On success (200) the account is **soft-deleted and anonymized**: its email/name are scrubbed to `deleted-<accountId>@deleted.invalid`, the password hash is cleared, all refresh tokens are revoked, and its organizer roles are detached from every event. The `AccountId` bridge is kept so the immutable event log stays consistent.

Verify:
- **Login is dead** — `POST /api/accounts/login` with the old credentials → 401 (non-enumerating).
- **Re-auth is enforced** — a wrong `password` (or bad/missing `totp` when MFA is on) → 401; nothing is deleted.
- **Sole-admin guard** — if you are the only Full Admin of any event, deletion is refused with **409 `Account.SoleFullAdmin`** naming those events. Add or promote another Full Admin (US-108/109), then retry. This mirrors the last-admin guard so no event is ever orphaned.

There is **no** endpoint to delete *another* user's account — the system has no global admin role (organizer authority is per-event).
