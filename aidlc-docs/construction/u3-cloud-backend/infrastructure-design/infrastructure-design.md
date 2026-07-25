# U3 Cloud Backend — Infrastructure Design

**Stage**: CONSTRUCTION → Infrastructure Design · **Unit**: U3 Cloud Backend
**Date**: 2026-07-25 · Provider-agnostic Docker Compose topology (NFR-6.4, no cloud IaC in MVP). Decisions: Q1=A (Caddy TLS proxy), Q2=A (backup sidecar), Q3=A (env-var secrets), Q4=A (dev+prod), Q5=A (stdout logs + /metrics).

---

## 1. Logical component → infrastructure mapping

| Logical component (NFR Design) | Infrastructure | Notes |
|---|---|---|
| ASP.NET Core Web API (all controllers/services) | **`api` container** (single instance, SC-1) | .NET 10 runtime image, pinned base tag; non-root user |
| `PostgresEventStore`, projections, Identity, idempotency/token stores | **`db` container** — PostgreSQL | Data on an **encrypted volume** (Q5/NFR-2.3); not exposed outside the internal network |
| TLS termination (NFR-2.2) | **`proxy` container — Caddy** (Q1=A) | Terminates TLS, forwards to `api` on internal net; auto-cert (prod) / local cert (dev). Omitted when a platform LB terminates TLS |
| Backups (NFR-3.10) | **`backup` sidecar** (Q2=A) | Cron `pg_dump` → gzip → encrypt → `backups` volume; retention sweep ≥30d |
| Secrets (NFR-2.6) | **Env vars** (Q3=A) | `.env` (git-ignored) via `env_file` in dev; provider secret manager in prod |
| Logs/metrics (NFR-2.7/3.9) | **stdout + `/metrics`** (Q5=A) | Collected by `docker logs`/platform; scrape `/metrics`; no bundled stack |
| Messaging | **none** | Synchronous inline projections (PP-2) — no broker/queue |

## 2. Container inventory

```
┌────────────────────── Docker Compose network (internal) ──────────────────────┐
│                                                                               │
│   [ proxy: Caddy ] ──TLS 443──▶ [ api: eventmanager-backend ] ──▶ [ db: postgres ]│
│        │                              │  :8080 (HTTP, internal)      │  :5432    │
│        │                              │                              │           │
│        │                         reads env secrets            encrypted volume    │
│        │                                                     [ pgdata ]           │
│                                                                   ▲               │
│                                          [ backup sidecar ] ──────┘ pg_dump       │
│                                                 └──▶ [ backups volume ]            │
└───────────────────────────────────────────────────────────────────────────────┘
   only :443 (proxy) published to host; api/db not exposed externally
```

| Service | Image (pinned) | Ports | Volumes | Depends on |
|---|---|---|---|---|
| `proxy` | `caddy:<pinned>` | `443:443` (host) | `caddy_data`, `caddy_config`, `Caddyfile` | `api` |
| `api` | `eventmanager-backend:<version>` (built) | internal `8080` | — | `db` (healthy) |
| `db` | `postgres:<pinned>` | internal `5432` | `pgdata` (encrypted) | — |
| `backup` | `postgres:<pinned>` (reuses client) or small cron image | — | `pgdata` (ro via network), `backups` | `db` |

## 3. Networking & isolation
- Single internal Compose network; **only the proxy's :443 is published** to the host. `api` and `db` are not reachable from outside.
- `db` accepts connections only from the Compose network (no host port mapping in prod).
- Health-gated startup: `api` waits for `db` healthcheck; `proxy` waits for `api`.

## 4. Storage & data lifecycle
- **`pgdata`** on an **encrypted volume** (Q5): encrypted Docker volume locally / provider-managed encrypted disk in prod.
- **`backups`** volume holds daily encrypted `pg_dump` archives; retention sweep keeps ≥30 days (NFR-3.10). Offsite copy is a documented prod add-on.
- **Migrations**: EF Core migrations applied on deploy in **expand/contract** order (RP-5); never destructive auto-migrate at runtime.

## 5. Resource sizing (MVP envelope — 300 athletes, hundreds concurrent registration burst)
| Service | vCPU (req/limit) | Memory | Rationale |
|---|---|---|---|
| `api` | 0.5 / 2 | 512Mi / 2Gi | Burst-oriented; async I/O; single instance |
| `db` | 0.5 / 2 | 1Gi / 4Gi | Registration burst + projection folds; tune `shared_buffers`/pool |
| `proxy` | 0.1 / 0.5 | 64Mi / 256Mi | TLS only |
| `backup` | idle | 64Mi | Runs briefly once/day |

Npgsql connection pool sized to the single API instance (e.g., max pool ~50–100), well within the burst envelope.

## 6. Environments (Q4=A)
| Env | How | Secrets | TLS |
|---|---|---|---|
| **dev** | Compose on developer machine (`docker compose up`) | `.env` (git-ignored) | Caddy local cert / `localhost` |
| **prod** | single deploy target (VPS/ECS/ACI); direct/in-place deploy | provider secret manager / injected env | Caddy auto-cert **or** platform LB |
Staging is out of scope for MVP.

## 7. Provider-agnostic host variations (documented, not built as IaC)
- **Bare VPS**: run the full Compose stack incl. `proxy` + `backup`; encrypted disk via host.
- **AWS ECS / Azure ACI**: platform LB may terminate TLS (omit `proxy`); managed encrypted disk + managed snapshots complement the `backup` sidecar; secrets via the platform secret store.
- No Terraform/CloudFormation/Bicep in MVP (NFR-6.4) — the Compose file is the reference deployment artifact.

## 8. Relationship to existing `backend/`
`backend/` already holds U8 `EventManager.Payments` + `EventManager.Backend.slnx`. U3 **adds the API project** (e.g., `EventManager.Api`) and infra files (`Dockerfile`, `docker-compose.yml`, `Caddyfile`, backup script, `.env.example`) to the **same** `backend/` tree/solution — not a new service. The API project references U1/U2/U8 projects.

## Extension compliance summary
| Extension | Status | Rationale |
|---|---|---|
| Security Baseline | **Compliant** | TLS at proxy (S1), encrypted volume (S2), env-injected secrets (S11), db not externally exposed, non-root container, pinned base image (S13). |
| Resiliency Baseline | **Compliant** | Backup sidecar + retention (R8), health-gated startup + `/health` (R6), expand/contract migrations (R7), single-region multi-host-capable. Warm standby **N/A** (D-02). |
| Property-Based Testing | **N/A** | No infra-level properties; PBT applies to code logic. |
