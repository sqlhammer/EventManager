# U10 HTTP Replication Adapter — Verification Guide

**Unit**: U10 · **Branch**: `unit/u10-http-replication`

This guide is not supplementary. Q11=D made a **manual docker-compose walkthrough the primary
integration verification for this unit**, so §3 is where hub→cloud replication is actually proven over
a network. The automated suites below cover the halves; the walkthrough covers the seam in the real
runtime.

---

## 1. Automated verification

```bash
dotnet build backend/EventManager.Backend.slnx
dotnet test  backend/EventManager.Backend.slnx        # cloud: credentials, ingest, provenance
dotnet test  admin/EventManager.Admin.slnx            # hub: classification, breaker, custody, P-REPL-1
dotnet test  EventManager.Integration.slnx            # the seam: real adapter → real ingest endpoint
```

**`EventManager.Integration.slnx` is new and easy to miss** — it is a sixth solution, added so the
credential-path test actually runs. A verification sweep that iterates the five original solutions
will silently skip the one test that proves a real credential reaches the real endpoint.

| Suite | What it proves | What it does not |
|---|---|---|
| Backend | Issue/revoke/expiry, cap of 3, scope refusal, provenance | Nothing over a network |
| Hub | Classification table, breaker transitions, retry selectivity, credential custody, P-REPL-1 | The cloud is stubbed |
| Integration | A real credential, through the real adapter, to the real controller | Only the credential path; not the full event flow |

---

## 2. Reading the property test

`P-REPL-1` (in `ReplicationAdapterTests`) asserts the invariant the whole unit exists to preserve:

> For any interleaving of outages, server errors, and batch splits, the cloud's log is per device a
> **gap-free prefix** of the hub's log, with **no duplicates**.

If this fails, do not treat it as flaky. It means replication can lose or duplicate an event, which
is the one thing the offline-first design promises cannot happen.

---

## 3. Manual walkthrough (the primary integration verification)

### 3.1 Start the cloud

```bash
cd backend
cp .env.example .env
```

**Generate the two secrets.** `.env` is git-ignored (`.gitignore:13`), so generated values stay local.
Run each command once per value — never reuse one secret for both.

PowerShell:
```powershell
# 256-bit secret, hex (64 chars)
[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# or base64url (43 chars) — no '+', '/' or '=' padding
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).Replace('+','-').Replace('/','_').TrimEnd('=')
```

bash / macOS / Linux:
```bash
openssl rand -hex 32
```

Paste the results into `.env`:

```bash
JWT_SIGNING_KEY=<first value>       # 32+ chars required; the API throws at startup without it
METRICS_TOKEN=<second value>        # gates the /otlp/* route at Caddy
```

**Use hex or base64url, not plain base64, for `METRICS_TOKEN`.** The hub passes it through
`OTEL_EXPORTER_OTLP_HEADERS`, whose format is a comma-separated list of `key=value` pairs — a token
containing `=` (base64 padding) or `,` lands in a position where parsing depends on the SDK splitting
on the *first* `=` rather than every one. Hex and base64url avoid the question. Also avoid `$` in
either value: docker compose interpolates it inside `.env`.

`JWT_SIGNING_KEY` has no such constraint — it is UTF-8 bytes fed straight to HMAC-SHA256 — but the
same generators are fine for it.

**Rotation, when you come to it:**
- Changing `JWT_SIGNING_KEY` invalidates every issued access token immediately. Refresh tokens survive (they are random values hashed in the database, not signed), so clients recover by refreshing rather than signing in again.
- Changing `METRICS_TOKEN` breaks metrics export for **every** hub at once — each needs its `OTEL_EXPORTER_OTLP_HEADERS` updated. **Replication is unaffected**: that uses the per-hub credential, which is revoked individually.

Then start the stack:

```bash
docker compose up -d --build
```

**Use `--build`.** A stale image is the failure mode this project has already lost time to once: a
container predating the new routes matches a URL but finds no method and returns **405**. If you see
a 405 on a route that exists in source, check `docker ps` for a container older than your last build
before looking anywhere else.

The stack now includes `otel-collector`. It publishes no port — it is reached only through Caddy at
`/otlp/*`. `docker compose ps` should show it running; `docker compose logs otel-collector` should
show the metrics pipeline starting with no errors.

### 3.2 Start the hub

```bash
dotnet run --project admin/EventManager.Hub      # http://localhost:5000
```

`GET http://localhost:5000/health` should now include a `replication` block reporting
`credentialInstalled: false`. **A hub with no credential is not an error state** — it runs the event
normally and simply does not replicate.

### 3.3 Issue and install a credential (Postman folder "Hub Replication (U10)")

Run requests **31 → 43** in order. Request 31 captures the one-time key into `{{hubCredentialKey}}`;
it is shown once and cannot be retrieved again, so if you lose it, issue another.

| # | Expect |
|---|---|
| 31 | 200, a `key`, and a visible `expiresAt` (event date + 14 days) |
| 32 | 200, listing with **no** key material |
| 33 | 200, `acceptedCount` > 0 |
| 34 | 200, `acceptedCount` **0** — replay accepts nothing |
| 35 | 200, per-device cursors |
| 36 | **401/403** — foreign scope refused, and nothing from that batch stored |
| 37 | **401/403**, and the body must not say *why* |
| 38 | 200 — revoked |
| 39 | **401/403** immediately — no session, no cache |
| 40–43 | Hub-side install, status, close-out, clear |

### 3.4 The scenarios this unit unblocked

`construction/build-and-test/integration-test-instructions.md` had **Scenario 2** (hub→cloud
replication over HTTP) and **Scenario 4** (the offline-first loop) marked ⛔ blocked on this adapter.
Both are now runnable:

**Scenario 2 — replication over HTTP**
1. With a credential installed, post a sync batch to the hub (Admin Hub folder).
2. Within ~60 seconds (drain timer), `GET /api/replication/status` should show `pendingEvents: 0`.
3. Cross-check with cloud request 35: the cursor should have advanced.

**Scenario 4 — the offline-first loop (the flagship)**
1. Stop the cloud: `docker compose stop proxy api`.
2. Keep working on the hub — pair a spoke, post sync batches. **Nothing should error.**
3. `GET /api/replication/status`: `pendingEvents` climbs, `consecutiveFailures` rises, and after 3
   connection failures `circuitState` becomes `Open`. This is the correct, healthy response to an
   outage — not a fault.
4. Restart the cloud: `docker compose start api proxy`.
5. Within the cool-down (60s) plus one drain interval, the backlog should drain to zero on its own,
   with no duplicates in the cloud.
6. `POST /api/replication/close-out` → `fullyReplicated: true`, `outstanding: 0`.

Step 3 is the point of the whole unit: **an outage is a non-event.** If anything at the venue surfaces
an error during step 2, that is a defect regardless of what the tests say.

### 3.5 Restart resilience (US-805)

With a backlog partly replicated, stop the hub (Ctrl+C) and `dotnet run` it again. It should seed its
cursors from the cloud and resume — not re-send the whole event. Watch the log for
`Seeded replication cursors for N devices`. If the cloud is unreachable at start-up you should
instead see the "starting with empty cursors" message, and the hub should start anyway.

### 3.6 Metrics (optional; replication does not depend on it)

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=https://localhost/otlp
export OTEL_EXPORTER_OTLP_HEADERS="Authorization=Bearer <METRICS_TOKEN>"
export OTEL_SERVICE_NAME=eventmanager-hub
dotnet run --project admin/EventManager.Hub
```

`docker compose exec otel-collector wget -qO- localhost:8889/metrics | grep eventmanager` should show
the replication instruments. A wrong token yields **401 at Caddy** and the collector never sees it.

**Two things to be clear about, because both are easy to misread as bugs:**
- There is **no metrics retention**. The collector exposes current values for scraping; nothing scrapes it, so nothing is kept. This unit delivers a pipeline, not a history.
- The collector is in the **cloud**, so during an outage it receives nothing. **Silence there means "the hub cannot report", never "the hub is fine."** The venue-visible signal is the hub's own `/health`.

---

## 4. Known limitations

| Limitation | Where recorded |
|---|---|
| DPAPI is `CurrentUser` — running the hub as a service under a different account fails cleanly as "no usable credential"; re-install under the running account | P-10 |
| One shared metrics token across all hubs; rotating it invalidates every hub at once | ID-Q2=A |
| The Caddy token check is not constant-time | Infrastructure Design §3 |
| No event check at credential install — a wrong-event credential surfaces on first replication, not at install | CL-A |
| `hub.db` as a whole is still unencrypted; only the credential is protected | D-09 |
| Collector config and Caddyfile were **not executed-verified** during code generation (no Docker daemon available) — §3 is where they get proven | this guide |
