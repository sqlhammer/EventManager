# EventManager — Postman collection

HTTP coverage for the whole MVP, mirroring [`aidlc-docs/testing-guide.md`](../aidlc-docs/testing-guide.md). Two hosts are exercised:

- **Cloud backend** (U3 + U8) — `{{cloudBaseUrl}}` (default `https://localhost`, the TLS proxy from `docker compose up`).
- **Admin hub** (U4a + U4b) — `{{hubBaseUrl}}` (default `http://localhost:5000`, from `dotnet run --project admin/EventManager.Hub`).

> Units with **no HTTP surface** — U7 offline resilience and the U5 Judge / U6 Check-In app-cores — are verified with `dotnet test` (testing-guide §4–§5). There is nothing to call for them here.

## Files
| File | What it is |
|---|---|
| `EventManager.postman_collection.json` | The collection (Postman schema v2.1.0). |
| `EventManager.local.postman_environment.json` | Local environment with the base URLs and empty capture slots. |

Import both into Postman, then select the **EventManager — Local** environment.

## Setup
1. **Bring the services up** (see testing-guide §2–§3):
   ```bash
   cd backend && cp .env.example .env   # edit secrets
   docker compose up --build            # cloud: proxy(TLS) + api + db + backup
   dotnet run --project admin/EventManager.Hub   # hub (separate terminal)
   ```
2. **Trust the dev TLS cert.** The cloud proxy uses a self-signed cert. In Postman, turn **Settings → General → SSL certificate verification** *off* (the collection's `curl -k` equivalent).
3. If the hub binds to a different port (check its console `Now listening on:` line, or `ASPNETCORE_URLS`), update `hubBaseUrl`.

## Run order
The requests chain through collection variables that **test scripts capture automatically**:

1. **Accounts & Auth** → `Register` → copy the confirmation token from the **EmailOutbox** DB table into `confirmToken` → `Confirm email` → `Login` (captures `accessToken` + `refreshToken`; all later cloud calls inherit Bearer auth).
2. **Events & Divisions** → `Create event` (captures `eventId`) → `Configure division` (captures `divisionId`) → policies → `Open registration`.
3. **Registration** → `Athlete profile` (captures `athleteId`) → `Register athlete` (captures `registrationId`) → bulk / payment.
4. **Organizers**, **Ingest**, **Results** as needed.
5. **Admin Hub** → `Issue pairing token` (captures `enrollmentToken`) → `Redeem` (captures `deviceId`) → device mgmt / `Sync batch` → **Competition** (`mat-assignments` → `bracket:start` → `bracket:advance` → `standings` → `finalize`, plus disputes). Set `matchId`/`winnerId` from live bracket state before advancing.

A few ids can't be inferred from a single response (`coOrganizerAccountId`, `matchId`, `winnerId`) — set those by hand from earlier responses / DB state. Capture scripts log to the Postman console when they can't auto-detect a field.

## Notes
- **Auth model:** the collection sends `Authorization: Bearer {{accessToken}}` by default. Anonymous cloud endpoints (register / confirm / login / refresh / health) and the entire hub folder override to **No Auth** — the hub's LAN surface is unauthenticated by design (LAN trust model).
- **Stubbed by design:** email (written to the EmailOutbox table) and card payments (U8 stub — no live charge).
- **Negative checks** from the guide (lockout on repeated bad logins, 409 on closed-window registration, 403 on foreign-mat scores, 429 rate limits) are reproducible by re-sending the relevant request with altered inputs; one example — *Register → breached password* — is included explicitly.
- Enum values used in bodies: `weighInPolicyMode` ∈ {Strict, AutoMove, Tolerance}; `format` ∈ {SingleElimination, RoundRobin}; `newRole` ∈ {FullAdmin, CoOrganizer}; payment `status` ∈ {Paid, Owed, Waived}; match `method` ∈ {Points, Forfeit, Disqualification, Decision}.
