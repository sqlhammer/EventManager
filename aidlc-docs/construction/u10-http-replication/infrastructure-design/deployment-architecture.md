# U10 — Deployment Architecture

**Unit**: U10 HTTP Replication Adapter · **Stage**: Infrastructure Design

---

## 1. Topology

```text
   ┌──────────────── VENUE (behind NAT, often offline) ────────────────┐
   │                                                                   │
   │   Spokes (judge / check-in)  ──LAN──►  HUB                        │
   │                                        ├─ hub.db (SQLite)         │
   │                                        ├─ credential (DPAPI)      │
   │                                        └─ /health  ◄── organizer  │
   │                                              (works with no WAN)  │
   └──────────────────────────┬────────────────────────────────────────┘
                              │  outbound only — the hub is never
                              │  reachable from the cloud
                              ▼
                        ═══ internet ═══
                              │
                       ┌──────▼──────┐
                       │   :443      │   caddy:2.8   (only published port)
                       │   proxy     │   TLS · access log (JSON) · token gate
                       └──┬───────┬──┘
              /otlp/*     │       │   everything else
           (Bearer token) │       │
              ┌───────────▼─┐   ┌─▼──────────┐
              │ otel-       │   │   api      │  expose 8080
              │ collector   │   │            │
              │ expose 4318 │   └─────┬──────┘
              │        8889 │         │
              └─────────────┘   ┌─────▼──────┐      ┌──────────┐
                                │    db      │◄─────│  backup  │
                                │ postgres17 │      │  daily   │
                                └────────────┘      └──────────┘
```

**Text alternative**: the venue side (spokes, hub, hub database, DPAPI-protected credential, local
`/health`) sits behind NAT and initiates all traffic outbound. The cloud side publishes exactly one
port, `443`, on a Caddy proxy that terminates TLS and writes JSON access logs. Caddy routes `/otlp/*`
to the OTLP collector after checking a bearer token, and everything else to the API. Collector, API,
database, and backup all sit on the internal network with no published ports.

**The direction of every arrow crossing the internet is hub → cloud.** That is what makes the whole
design work behind NAT, and it is why credential revocation takes effect on the hub's next attempt
rather than being pushed to it.

---

## 2. Ports

| Service | Port | Published | Reached by |
|---|---|---|---|
| `proxy` | 443 | **Yes — the only one** | Hubs, organizers, registrants |
| `api` | 8080 | No (`expose`) | Caddy |
| `otel-collector` | 4318 | No (`expose`) | Caddy, `/otlp/*` only |
| `otel-collector` | 8889 | No (`expose`) | Future scraper, internal |
| `db` | 5432 | No | `api`, `backup` |

---

## 3. Deployment

Unchanged from U3: `cd backend && docker compose up -d --build`.

| Step | Note |
|---|---|
| 1. Set `METRICS_TOKEN` in `.env` | New. The stack has no default and will not start without one — deliberate, per SECURITY-09 |
| 2. `docker compose up -d --build` | Brings up the collector alongside existing services |
| 3. EF migration applies at startup | Development only, as today. The `HubCredentials` table and the `EventRecord` provenance column arrive here |
| 4. Issue a hub credential | `POST /api/events/{id}/hub-credentials` — the key is shown **once** |
| 5. Install it on the hub | `POST /api/replication/credential` |
| 6. Set the hub's `OTEL_EXPORTER_OTLP_*` variables | Optional; replication works without metrics |

Step 6 being optional is deliberate: **metrics are not a dependency of replication**. A hub with no
OTLP configuration replicates normally.

---

## 4. Rollback

| Change | Reversal | Difficulty |
|---|---|---|
| Collector service | Remove from Compose | Trivial |
| Caddy route + logging | Revert the Caddyfile | Trivial |
| Hub-side code | Re-register the in-process transport | Easy |
| **EF migration** | Migration down | **Moderate — this is why the unit's rollback rating is Moderate, not Easy** |

The migration is additive (a new table, a nullable column), so a down-migration loses only the
credential records and provenance data — no event data is at risk. But it is a schema change, and the
execution plan rated rollback Moderate for exactly this reason.

---

## 5. Failure modes

| Failure | Effect on replication | Effect on the event |
|---|---|---|
| Collector down | **None** — the exporter fails independently of the transport | None |
| Metrics token wrong | None — exports rejected at Caddy | None |
| Caddy down | Replication stops; hub queues locally | **None** — the hub is authoritative |
| API down | Replication stops; hub queues locally | None |
| Database down | Ingest fails; classified transient; hub retries | None |
| Venue internet down | Replication is a no-op; breaker opens | **None** — this is the flagship case |
| Hub credential revoked | Replication stops **permanently** until re-installed; reported distinctly from an outage | None — the hub keeps running the event (`BR-REPL-18`) |

Every row's third column is "None". That is the property the whole system is built around, and this
unit does not weaken it: **nothing in the cloud can stop a tournament from running.**

---

## 6. What Operations inherits

Recorded so the boundary is explicit rather than assumed.

- A scraper and storage for the Prometheus exposition endpoint — **without one, nothing is retained.**
- Dashboards and alert rules on `eventmanager.replication.*`.
- `METRICS_TOKEN` rotation, which invalidates all hubs at once (ID-Q2=A).
- Log shipping and retention — Caddy now *writes* access logs (ID-Q3=A), but they go to stdout and nothing collects them. **Writing logs is not retaining them**, and the 90-day retention SECURITY-14 asks for remains an open project-level gap.
- Scenarios **R-7** (cloud down for hours) and **R-8** (collector unavailable) from the NFR Design testing table, which this unit deliberately did not claim.
