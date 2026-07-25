# U3 Cloud Backend — Infrastructure Design Plan

**Stage**: CONSTRUCTION → Infrastructure Design (per-unit)
**Unit**: U3 Cloud Backend (`backend/`)
**Date**: 2026-07-25 · Stage-by-stage

---

## Context: infra largely fixed by baseline

| Category | Fixed by baseline | Ref |
|---|---|---|
| **Deployment env** | **Provider-agnostic**; Docker image + **Docker Compose**; **no cloud IaC in MVP** | NFR-6.4, D-10 |
| **Compute** | Single API container (NFR Design SC-1, Q1=A); direct/in-place deploy, brief downtime OK | NFR-3.6 |
| **Storage** | **PostgreSQL** container; **volume-level at-rest encryption** (Q5=A) | tech-env, NFR-2.3 |
| **Messaging** | **None** — synchronous inline projections (Q2=A); no queue/broker | NFR Design PP-2 |
| **Monitoring** | Structured stdout logs (no PII); metrics (ingest depth/latency/errors); `/health` + deep DB probe | NFR-2.7/3.7/3.9 |
| **Backups** | Automated **daily PostgreSQL backup**, encrypted, retention ≥ 30 days; documented restore | NFR-3.10 |
| **CI/CD** | GitHub Actions build+test+coverage; **pinned Docker base image**; SBOM; version-pinned redeploy + rollback | NFR-3.4/3.5/2.9 |

So this stage produces `infrastructure-design.md` + `deployment-architecture.md` around a **Compose topology** and asks only the open deployment-parameter choices below. (Cloud-provider selection is deliberately **out of scope** — provider-agnostic by mandate.)

---

## Open decisions (please answer inline)

> Multiple-choice, `[Answer]:` tag; recommendation **(rec)**.

### Q1 — TLS termination in the provider-agnostic topology (NFR-2.2)
All API traffic must be TLS 1.2+. Where is TLS terminated in the reference Compose stack?

- **A. (rec)** Include an optional **reverse-proxy container (Caddy)** in the Compose stack that terminates TLS (auto-cert in prod, local cert in dev) and forwards to Kestrel over the internal network. In managed hosting, the platform LB may terminate TLS instead and the proxy is omitted. Kestrel can also run HTTPS directly for bare-VPS. Documents all three, defaults to the proxy.
- **B.** **Kestrel terminates TLS directly** (cert mounted via secret), no proxy. Fewer containers; you manage cert rotation in-app.
- **C.** Assume an **external/platform LB** always terminates TLS; the container serves HTTP internally only. Simplest stack, but not self-contained for a bare VPS.

[Answer]: A

### Q2 — Backup mechanism (NFR-3.10)
Daily encrypted PostgreSQL backups, ≥30-day retention.

- **A. (rec)** **Backup sidecar container** (scheduled `pg_dump`, gzipped + encrypted, written to a mounted `backups` volume) with a retention sweep; provider-managed disk snapshots used additionally when the host supports them. Self-contained, provider-agnostic, matches RPO≤24h.
- **B.** Rely solely on **provider-managed automated snapshots** (no sidecar). Simpler, but not self-contained and unavailable on a bare VPS.
- **C.** Manual/documented-only backup for MVP. Weakest; risks the RPO target.

[Answer]: A

### Q3 — Secrets injection (NFR-2.6)
Connection strings, JWT signing key, etc.

- **A. (rec)** **Environment variables injected at deploy** — `.env` file (git-ignored) for local/dev via Compose `env_file`; **provider secret manager** in production. Never in source or image. Matches NFR-2.6 exactly.
- **B.** Docker/Compose **secrets** (mounted files) for all environments. Also valid; slightly more setup for local dev.

[Answer]: A

### Q4 — Environments in scope for MVP
- **A. (rec)** **Two**: local **dev** (Compose on the developer machine) + a single **production** deploy target. A staging env is optional/out of scope for MVP.
- **B.** **Three**: dev + staging + production (adds a staging pipeline now).

[Answer]: A

### Q5 — Log/metrics destination
- **A. (rec)** **Structured logs to stdout** (collected by the platform/`docker logs`); metrics exposed on a `/metrics` endpoint for scraping; **no self-hosted observability stack** (Prometheus/Grafana/ELK) bundled in MVP — a hook/seam is documented. Matches "no cloud IaC in MVP."
- **B.** Bundle a monitoring stack (e.g., Prometheus + Grafana) in Compose now. Heavier; premature for MVP.

[Answer]: A

### Q6 — Anything else / overrides?
Free-form: resource sizing hints, network isolation specifics, container-registry choice, or constraints.

[Answer]: N/A

---

## Execution checklist (after answers approved)

- [x] Q1–Q5 answered (all A) + Q6 N/A
- [x] `infrastructure-design/infrastructure-design.md` — component→infra mapping, container inventory (proxy/api/db/backup), networking/isolation, storage lifecycle, sizing, environments, provider-agnostic host variations, relationship to existing backend/, extension compliance
- [x] `infrastructure-design/deployment-architecture.md` — topology, CI build pipeline, deploy/rollback flow, expand/contract migration ordering, health wiring, backup/restore runbook, secrets, observability, infra artifacts list
- [x] Noted `backend/` already holds U8 Payments + solution — U3 adds API project + infra to same tree
- [x] Extension compliance summary (Security/Resiliency Compliant; PBT N/A at infra level; warm standby N/A)
- [ ] Completion message; await explicit approval
