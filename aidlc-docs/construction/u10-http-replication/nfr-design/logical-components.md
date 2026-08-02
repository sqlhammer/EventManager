# U10 — Logical Components (NFR-bearing)

**Unit**: U10 HTTP Replication Adapter · **Stage**: NFR Design

The components that exist to satisfy a non-functional requirement, and how they integrate. Functional
components are in `inception/application-design/u10-components.md`; this document covers what carries
the resilience, security, performance, and observability properties.

---

## 1. Cloud side

| Component | Type | Pattern | Integration |
|---|---|---|---|
| `"ingest"` rate-limit policy | Configuration on the existing limiter | P-5 | Added to `AddRateLimiter` beside `login` and `registration`. Partitions by **hash of the presented credential header** (P-8), because the limiter runs before authentication |
| Ingest concurrency limiter | Configuration on the existing limiter | P-4 | Global cap of 8 in-flight ingest requests |
| `OnRejected` handler | Delegate on the limiter | P-9 | Reads retry-after metadata, writes the `Retry-After` header. **New behaviour** — the API does not emit it today |
| Ingest body-size limit | Route metadata | P-12 | 8 MB, twice the hub's 4 MB client cap |
| `HubCredentialAuthenticationHandler` | Auth scheme | P-7, P-11 | Registered beside JWT; evaluated per request, so revocation is immediate |

**Integration note**: everything except the auth handler is configuration on components that already
exist. The cloud gains one new authentication scheme, not a new middleware stack.

---

## 2. Hub side

| Component | Type | Pattern | Integration |
|---|---|---|---|
| `ReplicationOptions` | Bound options class | §5 | `ValidateOnStart`; cross-field rule that max batch bytes < server body limit |
| `ISecretProtector` / `DpapiSecretProtector` | Seam + Windows implementation | P-10 | Interface in the library, implementation registered **only at the composition root** — keeps the library platform-neutral (U10-CON-1) |
| `ReplicationFailureClassifier` | Pure function | P-1 | No dependencies; directly unit-testable, which `P-REPL-1` relies on |
| `ReplicationCircuitBreaker` | State machine | P-2 | Drives `HttpCloudReplicationTransport.IsOnline`. Advanced **only** by connection failures |
| Retry policy | Inline in `ReplicationClient` | P-1 | Ladder unchanged from U7; only the *classification* gate is new |
| `HttpClient` via `IHttpClientFactory` | Framework | P-3 | Named client with the 30s timeout; handles socket and DNS lifetime |
| `ReplicationStatus` | Singleton | P-13, P-15 | Cached values for `/health` and metrics |
| Status query path | On-demand read | P-13 | `GET /api/replication/status` computes lag **and** pending in one store pass |
| `ReplicationMetrics` | `Meter` + instruments | P-14 | `eventmanager.replication.*`; component code references no OTel type |
| OTel exporter wiring | Composition root | P-14 | `OpenTelemetry.Extensions.Hosting` + OTLP exporter, host only |
| `ReplicationSignal` | Bounded channel | P-6, U10-NFR-8 | Non-blocking, drop-on-full — an append is never delayed by replication |

---

## 3. Infrastructure

| Component | Pattern | Owned by |
|---|---|---|
| OTLP collector (contrib), OTLP in → Prometheus exposition out | P-14 | **Infrastructure Design** — exact image tag (never `latest`), network exposure (SECURITY-07), access logging (SECURITY-02) |

---

## 4. How the controls layer

```text
                    ┌──────────────── cloud ────────────────┐
  hub request ─────►│ rate limit (P-5, per credential hash) │  pre-auth
                    │ concurrency cap (P-4, global)         │  pre-auth
                    │ body size limit (P-12)                │
                    │ authentication (P-7, P-11)            │
                    │ scope authorization (BR-REPL-10)      │
                    │ idempotent append                     │
                    └───────────────────────────────────────┘
                                     │
                            429 + Retry-After (P-9)
                                     ▼
                    ┌───────────────── hub ─────────────────┐
                    │ classify (P-1)                        │
                    │ retry transient only                  │
                    │ breaker on connection failures (P-2)  │
                    │ timeout every call (P-3)              │
                    │ degrade to no-op (P-6)                │
                    └───────────────────────────────────────┘
```

Four independent controls stand between an abusive caller and the database — rate limit, concurrency
cap, body limit, authentication — satisfying SECURITY-11's defence-in-depth requirement without any
one of them being the sole line.

---

## 5. Deliberately absent

| Not used | Why |
|---|---|
| Polly / `Microsoft.Extensions.Http.Resilience` | TS-U10-5 — `BR-REPL-33/34` semantics would be configured around rather than used |
| Distributed cache for credential lookups | Revocation must be immediate (`BR-REPL-8`); a cache would introduce exactly the staleness US-808 forbids |
| Retry jitter | ND-Q4=A. Residual synchronization risk is covered server-side by P-4 — see the note in `nfr-design-patterns.md` §P-1 |
| Persistent replication cursors on the hub | The cloud is the authority; a local copy could disagree after a cloud-side restore |
| Hub-side metrics endpoint (`/metrics`) | F3=B chose cloud push. `/health` is the offline signal (P-15) |
