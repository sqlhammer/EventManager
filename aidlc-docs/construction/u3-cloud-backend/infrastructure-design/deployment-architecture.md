# U3 Cloud Backend — Deployment Architecture

**Stage**: CONSTRUCTION → Infrastructure Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Deploy/rollback flow, health wiring, backup/restore runbook, CI/CD steps for the provider-agnostic Compose stack.

---

## 1. Topology (reference Compose)

```
Internet ──▶ :443  [ proxy: Caddy ]  (TLS 1.2+ termination, HSTS)
                        │  reverse_proxy → api:8080 (internal HTTP)
                        ▼
                 [ api: eventmanager-backend ]
                   • ASP.NET Core Web API (.NET 10)
                   • JWT authn, rate limiter, RBAC authz
                   • EF Core (Npgsql) → db
                   • /health (liveness) + /health/ready (deep DB) + /metrics
                        │
                        ▼
                 [ db: postgres ]  ── pgdata (encrypted volume)
                        ▲
                 [ backup ] ─ daily pg_dump → encrypt → backups volume (≥30d)
```

Only `:443` is published. Deploy artifact = the Compose file + built API image; no IaC (NFR-6.4).

## 2. Build pipeline (GitHub Actions — NFR-3.4/2.9)

```
1. checkout
2. dotnet restore (locked versions)         ── supply chain (NFR-2.9)
3. dotnet build  (backend solution)
4. dotnet test   (xUnit + FsCheck, seeds logged)   ── PBT (NFR-4.2)
5. coverage gate ≥80% on core logic         ── block merge if below (NFR-4.4)
6. docker build  (pinned base image)        ── SBOM generated
7. vulnerability scan (image + deps)        ── SECURITY (NFR-2.9)
8. push image  eventmanager-backend:<git-sha / semver>   ── version-pinned
```

## 3. Deploy flow (direct/in-place — NFR-3.6)

```
1. pull eventmanager-backend:<new-version>
2. run EF migrations (expand phase — backward compatible)   ── RP-5
3. docker compose -f docker-compose.yml up -d api   (brief downtime acceptable by design)
4. health gate: wait /health/ready == healthy
5. smoke check (auth + a read endpoint)
6. (later, after verification) contract phase of migration on next release
```
**Always pass `-f docker-compose.yml` explicitly on prod hosts/CI.** This pins the compose file set so `docker-compose.override.yml` (dev-only, publishes the db port — §9) is never picked up even if it happens to be present, rather than relying on it simply not being deployed there.

**Migration ordering (expand/contract, RP-5/R7):** a release only adds/backfills (expand); columns/tables are dropped one release later (contract), so the previous image still runs against the new schema → **safe rollback**.

## 4. Rollback flow (NFR-3.5)

```
1. re-deploy previous pinned image tag  (docker compose -f docker-compose.yml up -d api)
2. schema is backward-compatible one version → no DB rollback needed
3. verify /health/ready + smoke check
```
RTO ≤ 4h target easily met (redeploy is minutes); if data restore needed, see §6.

## 5. Health-check wiring (NFR-3.7)
| Endpoint | Type | Used by |
|---|---|---|
| `/health` | liveness (process up) | orchestrator restart policy |
| `/health/ready` | readiness — **deep Npgsql DB connectivity** | deploy gate, LB/proxy upstream check |
| `/metrics` | metrics scrape (ingest depth, latency, errors) | platform monitoring (Q5) |

`db` service uses `pg_isready` healthcheck; `api` `depends_on: db: condition: service_healthy`.

## 6. Backup & restore runbook (outline — NFR-3.10/3.12)

**Backup (automated, daily):** `backup` sidecar runs `pg_dump` → gzip → encrypt (key from secret) → write `backups/em-YYYYMMDD.sql.gz.enc`; prune archives older than 30 days.

**Restore (manual runbook):**
```
1. stop api      (docker compose stop api)
2. decrypt + gunzip the chosen archive
3. psql restore into a fresh db volume  (or pg_restore)
4. run any pending migrations (expand)
5. start api; verify /health/ready + spot-check roster/results
```
RPO ≤ 24h for cloud-originated data (accounts/registrations). Event-day data has RPO≈0 — the hub re-replays its log via `EventIngestController` after any cloud gap (idempotent, RP-1).

## 7. Secrets (Q3=A)
| Secret | dev | prod |
|---|---|---|
| DB connection string | `.env` (git-ignored) | secret manager / injected env |
| JWT signing key | `.env` | secret manager |
| Backup encryption key | `.env` | secret manager |
`.env.example` (committed) documents required keys with placeholder values. Never commit real secrets or bake them into images (NFR-2.6).

## 8. Observability (Q5=A)
- Structured JSON logs to **stdout** (correlation id, level; no PII/secrets) — collected via `docker logs`/platform.
- `/metrics` exposes ingest depth / replication lag, request latency, error rate, auth-denial count.
- Alerting hooks documented; no Prometheus/Grafana/ELK bundled in MVP (seam noted).

## 9. Artifacts Code Generation will produce (infra)
`backend/EventManager.Api/Dockerfile` (multi-stage, pinned, non-root) · `backend/docker-compose.yml` (+ `docker-compose.override.yml` for dev) · `backend/Caddyfile` · `backend/backup/backup.sh` · `backend/.env.example` · `.github/workflows/backend.yml` (build/test/scan/push).

## Story/NFR traceability
Deploy/rollback→NFR-3.5/3.6 · health→NFR-3.7 · backups→NFR-3.10 · TLS→NFR-2.2 · secrets→NFR-2.6 · CI/scan/SBOM→NFR-3.4/2.9 · ingest re-replay RPO≈0→FR-4.6/US-504.
