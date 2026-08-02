# U10 — Infrastructure Design Plan

**Stage**: CONSTRUCTION → Infrastructure Design · **Unit**: U10 · **Branch**: `unit/u10-http-replication`
**Why this stage is mandatory here**: F3=B adds a collector service to the cloud Compose stack. It is the only infrastructure change in the unit, and it is the whole reason this stage was not skippable.

---

## Grounded before writing these questions

Read `backend/docker-compose.yml` and `backend/Caddyfile`. Four facts shape the questions:

| Fact | Consequence |
|---|---|
| **Only `proxy` publishes a port** (`443`). `api` uses `expose`; `db` and `backup` publish nothing | The stack is already deny-by-default (SECURITY-07). Any new service should keep it that way — ID-Q1 |
| **The hub is at a venue behind NAT** | It can make outbound connections but cannot be reached inbound. So the collector's OTLP receiver must be reachable **from the public internet** — this is not optional if hub metrics are to arrive at all |
| **OTLP receivers have no built-in authentication** | An internet-reachable collector with no gate accepts metrics from anyone who finds it — ID-Q2 |
| **The `Caddyfile` has no `log` directive** | Caddy v2 does not write per-site access logs unless configured. SECURITY-02 requires access logging on network intermediaries, so this is a **pre-existing gap**, not one this unit creates — ID-Q3 |

Existing images are pinned by version tag (`caddy:2.8`, `postgres:17`), never `latest` — SECURITY-10 satisfied in style if not by digest.

---

## PART 1a — Infrastructure Questions

---

### ID-Q1 — How does a venue hub reach the collector?

A) **Through the existing Caddy proxy** — a dedicated hostname or path routed to an internal-only collector. No new published port; the collector uses `expose`, exactly like `api`. TLS is terminated where it already is. **My lean** — it preserves the single-published-port property the stack already has.

B) **Publish the collector's OTLP port directly** (4317/4318) with TLS configured inside the collector. A second public entry point, a second TLS configuration to maintain, and a second thing to get wrong.

C) **Do not expose it** — the collector receives only from cloud-side services. Honest and simple, but hub metrics never arrive, which makes `U10-FR-18` and the whole F3=B decision inert.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ID-Q2 — What authenticates a hub to the metrics endpoint?

Whatever ID-Q1 chooses, an internet-reachable OTLP endpoint needs a gate. Metrics are low-value on their own, but an open ingest point is a resource-exhaustion and data-poisoning vector.

A) **Static bearer token**, checked by Caddy before proxying. One shared secret across hubs; rotation is a config change and a redeploy. Simple and fully understood. **My lean** — proportionate to what is being protected.

B) **`forward_auth` to the API**, validating the real hub credential. No second secret, and per-hub revocation comes for free — but it adds an API endpoint and an authentication round trip on **every metric export**, which is a lot of traffic for an audit trail nobody has asked for.

C) **Mutual TLS** with client certificates. Strongest; introduces certificate issuance and rotation for every hub, which is a bigger problem than the one being solved.

D) **No authentication** — rely on an obscure hostname. *Recorded so it is visibly rejected*: this would be a SECURITY-08 finding, and an unauthenticated internet-facing ingest point is exactly what that rule exists to prevent.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ID-Q3 — SECURITY-02 access logging (pre-existing gap)

The `Caddyfile` configures no access logging, so the only network intermediary in the system currently logs nothing. This predates U10.

A) **Add a `log` directive for the whole site** as part of this unit. Closes the gap for every route at once, costs about three lines, and this unit is already editing the Caddyfile. **My lean.**

B) **Add logging only for the metrics route**, leaving the API's routes unlogged. Narrower, and leaves the larger gap open while appearing to have addressed it.

C) **Leave it** — record as a project-level gap alongside SBOM. Defensible on scope grounds; the cost of fixing it is genuinely three lines.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ID-Q4 — Image pinning for the collector

A) **Exact version tag**, matching the existing style (`caddy:2.8`, `postgres:17`). Consistent with the repo. **My lean.**

B) **Digest pin** for the collector only. Stronger supply-chain guarantee, but inconsistent with every other service — one hardened service among three unhardened ones is more confusing than useful.

C) **Digest pin every service** in the stack. The right end state for SECURITY-10; clearly a project-wide change rather than something U10 should carry.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ID-Q5 — Protecting the collector from resource exhaustion

An internet-reachable receiver that accepts unbounded input can be pushed over.

A) **`memory_limiter` processor** in the collector pipeline, no Compose resource limits — matching the repo, where no service declares limits. **My lean.**

B) **`memory_limiter` plus Compose CPU/memory limits** on the collector. Better containment; introduces a pattern no other service follows.

C) **Neither** — rely on the process defaults.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### ID-Q6 — Where does the hub get its metrics endpoint and token?

A) **Hub configuration** (`appsettings` / environment), separate from the credential install. Metrics endpoint and token are deployment configuration, not per-event data, and they do not change when an event does. **My lean.**

B) **Part of the credential install payload**, alongside the key and cloud base URL. One provisioning step instead of two; couples an operational setting to an event-scoped credential, so rotating one forces touching the other.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## PART 1b — Resolved Decisions

| Question | Answer | Resolution |
|---|---|---|
| ID-Q1 Collector reachability | **A** | Routed through the existing Caddy proxy at `/otlp/*`; collector uses `expose` only. The stack keeps its single published port (443) |
| ID-Q2 Metrics authentication | **A** | Static bearer token checked by Caddy. One shared token across hubs; no per-hub metrics revocation |
| ID-Q3 Access logging | **A** | JSON access log for the whole site — **closes a pre-existing SECURITY-02 gap**, not one U10 created |
| ID-Q4 Image pinning | **A** | `otel/opentelemetry-collector-contrib:0.157.0` — verified as the current stable tag on 2026-07-27 (0.158.0 is nightly-only) |
| ID-Q5 Resource protection | **A** | `memory_limiter` processor, first in the pipeline as OpenTelemetry requires; no Compose resource limits, matching the repo |
| ID-Q6 Hub metrics config | **A** | Standard `OTEL_EXPORTER_OTLP_*` environment variables — no custom configuration code, and kept out of the per-event credential install |

**Decided without asking** (small, follows from ID-Q1=A): the receiver is **OTLP/HTTP on 4318**, not
gRPC on 4317. gRPC through a reverse proxy needs h2c on both sides; at a handful of exports per minute
its efficiency advantage buys nothing against the configuration risk.

---

## Categories evaluated and found not applicable

Recorded explicitly rather than skipped, per the stage's mandatory-evaluation directive.

| Category | Status |
|---|---|
| Deployment environment | **No change.** Provider-agnostic Docker Compose, per U3 (NFR-6.4). This unit adds a service, not a platform |
| Compute infrastructure | **No change.** No sizing or scaling change; the collector is one small container |
| Storage infrastructure | **No change.** The credential table lives in the existing Postgres; no new volume. *Note*: the Prometheus-exposition pipeline holds current values in memory only — there is **no metrics retention** until Operations adds a scraper and storage |
| Messaging infrastructure | **N/A.** Replication is direct HTTP; the only queue is an in-process channel |
| Networking infrastructure | **Covered by ID-Q1/Q2.** No load balancer or gateway change beyond the Caddy route |
| Shared infrastructure | **N/A.** Single-tenant stack; no multi-tenancy or isolation change |

---

## PART 2 — Execution Checklist

- [x] Generate `construction/u10-http-replication/infrastructure-design/infrastructure-design.md`
- [x] Generate `construction/u10-http-replication/infrastructure-design/deployment-architecture.md`
- [x] Specify the collector service definition: image and pin, ports/expose, volumes, config file, restart policy
- [x] Specify the collector pipeline: receivers, processors, exporters
- [x] Specify the Caddy changes: route, authentication gate, logging per ID-Q3
- [x] Specify every new environment variable and its `.env.example` entry
- [x] Record retention and its absence honestly
- [x] Assess SECURITY-01/02/07/09/10 and RESILIENCY-05/08 for the new service
- [x] Confirm U10-CON-2 still stands and is restated where an operator will see it
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting
- [x] Mark every checklist item [x] in the same interaction as the work
