# U10 — Infrastructure Design

**Unit**: U10 HTTP Replication Adapter · **Stage**: Infrastructure Design
**Answers**: ID-Q1=A, ID-Q2=A, ID-Q3=A, ID-Q4=A, ID-Q5=A, ID-Q6=A

The unit's only infrastructure change: one collector service, one Caddy route, one access-log
directive. Nothing else in the stack moves.

---

## 1. New service — `otel-collector`

```yaml
otel-collector:
  image: otel/opentelemetry-collector-contrib:0.157.0
  command: ["--config=/etc/otelcol/config.yaml"]
  volumes:
    - ./otel-collector-config.yaml:/etc/otelcol/config.yaml:ro
  expose:
    - "4318"   # OTLP/HTTP receiver — internal only
    - "8889"   # Prometheus exposition — internal only
  restart: unless-stopped
```

**No `ports:` block.** The collector publishes nothing; it is reached only through Caddy, exactly
like `api`. The stack keeps its single published port (`443`), which is what made it SECURITY-07
compliant before this unit and keeps it compliant after.

**Tag `0.157.0`** — verified as the current stable release tag on 2026-07-27. `0.158.0` exists only
as nightly builds and is deliberately not used. Version-tag pinning matches `caddy:2.8` and
`postgres:17` (ID-Q4=A).

### Why OTLP/HTTP (4318) rather than gRPC (4317)

A decision, not a default. gRPC through a reverse proxy needs h2c configuration on both sides;
OTLP/HTTP is ordinary HTTP that Caddy proxies without special handling. Given the traffic — a handful
of metric exports per minute from a handful of hubs — gRPC's efficiency advantage buys nothing
against the configuration risk.

---

## 2. Collector pipeline

```yaml
receivers:
  otlp:
    protocols:
      http:
        endpoint: 0.0.0.0:4318

processors:
  memory_limiter:          # MUST be first in the pipeline (ID-Q5=A)
    check_interval: 1s
    limit_mib: 256
    spike_limit_mib: 64
  batch:

exporters:
  prometheus:
    endpoint: 0.0.0.0:8889

service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [prometheus]
  telemetry:
    logs:
      level: info
```

`memory_limiter` **must be the first processor** — that is an OpenTelemetry requirement, not a
stylistic choice. Placed later it would admit data it is meant to reject. It is what keeps an
internet-reachable receiver from being pushed over (ID-Q5=A).

**Minimal installation (SECURITY-09)**: no `zpages`, no `pprof`, no `health_check` extension, no
`debug` exporter. Each would be a diagnostic surface on an internet-adjacent service that nothing has
asked for.

---

## 3. Caddy changes

Indicative — exact directive syntax settles at Code Generation.

```caddy
{$SITE_ADDRESS:localhost} {
    encode gzip

    log {                                    # ID-Q3=A — closes the SECURITY-02 gap
        output stdout
        format json
    }

    header {
        Strict-Transport-Security "max-age=31536000;"
        -Server
    }

    handle /otlp/* {                         # ID-Q1=A
        @unauthorized not header Authorization "Bearer {$METRICS_TOKEN}"
        respond @unauthorized 401
        uri strip_prefix /otlp
        reverse_proxy otel-collector:4318
    }

    handle {
        reverse_proxy api:8080               # unchanged
    }
}
```

### Three honest notes

1. **The access-log directive closes a pre-existing gap.** The Caddyfile had no `log` block, so the
   only network intermediary in the system logged nothing. That predates U10; ID-Q3=A closes it for
   **every** route, not just the new one.
2. **Token comparison is not constant-time.** Caddy's header matcher does an ordinary string
   comparison, so it is theoretically timing-attackable. For a shared token guarding a metrics
   endpoint this is proportionate — but it is a real property of the design and is recorded rather
   than glossed. If metrics ever carry something sensitive, this is the first thing to revisit.
3. **One shared token across all hubs** (ID-Q2=A). There is no per-hub revocation for metrics;
   rotation means changing `METRICS_TOKEN` and redeploying, which invalidates every hub at once. The
   *replication* credential is per-hub and revocable — this is only the metrics gate.

---

## 4. Configuration

### New environment variables (cloud)

| Variable | Purpose | `.env.example` |
|---|---|---|
| `METRICS_TOKEN` | Bearer token for the `/otlp/*` route | Placeholder only — **never a real value** |

### New environment variables (hub)

Standard OpenTelemetry variables, so no custom configuration code is needed:

| Variable | Example | Notes |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `https://cloud.example.org/otlp` | Deployment configuration, **not** part of the credential install (ID-Q6=A) |
| `OTEL_EXPORTER_OTLP_HEADERS` | `Authorization=Bearer <token>` | Contains a secret — must not be logged or echoed by the hub's status routes |
| `OTEL_SERVICE_NAME` | `eventmanager-hub` | Identifies the source once the collector receives more than one |

Metrics endpoint and token are deployment settings that do not change when an event changes, which is
why ID-Q6=A keeps them out of the per-event credential install.

---

## 5. Security assessment for the new service

| Rule | Status |
|---|---|
| **SECURITY-01** Encryption in transit | **Compliant** — TLS terminates at Caddy for `/otlp/*` as for every other route. The Caddy→collector hop is plaintext **inside the Docker network**, identical to the existing Caddy→api hop. Stated rather than implied |
| **SECURITY-02** Access logging | **Now compliant** — ID-Q3=A adds JSON access logging for the whole site. Was a pre-existing gap |
| **SECURITY-07** Network configuration | **Compliant** — the collector publishes no port; the stack still exposes only `443` |
| **SECURITY-08** Access control | **Compliant** — the route is gated by a bearer token; unauthenticated requests are refused before reaching the collector. "No authentication" was explicitly considered and rejected at ID-Q2 |
| **SECURITY-09** Hardening | **Compliant** — minimal collector configuration, no diagnostic extensions, no default credentials (`METRICS_TOKEN` has no default and the stack fails without one) |
| **SECURITY-10** Supply chain | **Compliant** — official image, exact version tag, never `latest`. Digest pinning remains a project-wide improvement, not adopted here for one service alone (ID-Q4=A) |
| **RESILIENCY-05** Monitoring | **Compliant in scope** — the pipeline exists and terminates at a scrapeable endpoint. Dashboards and alert rules are Operations |
| **RESILIENCY-08** Topology | **Unchanged** — single-region, single-instance, as the rest of the stack |

**No blocking findings.**

---

## 6. Limitations, recorded rather than discovered later

- **No metrics retention.** The Prometheus exporter is an *exposition* endpoint: it holds current values in memory and serves them on scrape. With nothing scraping, nothing is retained. This unit delivers a pipeline, not a history.
- **The collector is a single container with no HA.** If it dies, metrics stop. **Replication is unaffected** — the exporter fails independently of the transport, by design.
- **U10-CON-2 still stands and is the important one.** The collector sits in the cloud, so during exactly the outages this unit exists to survive, it receives nothing. Silence in the cloud view means "the hub cannot report", never "the hub is fine". The venue-visible signal is the hub's own `/health` (P-15).
