# U10 — Component Dependencies

**Unit**: U10 HTTP Replication Adapter · **Stage**: Application Design

---

## 1. Dependency matrix

| Component | Depends on | Notes |
|---|---|---|
| `HubCredentialController` | `HubCredentialService`, `EventAuthorizer`, `CurrentUser` | JWT scheme (a person) |
| `HubCredentialService` | `AppDbContext`, password/key hashing from `Services/Security.cs` | Reuses existing hashing rather than inventing one |
| `HubCredentialAuthenticationHandler` | `HubCredentialService` | Registered as the `"HubCredential"` scheme |
| `EventIngestController` | `IngestService`, `CurrentCaller` | Accepts both schemes |
| `IngestService` *(amended)* | `AppDbContext`, `CloudProjectionHost`, `EventAuthorizer`, **`IngestCaller`** | Only the caller parameter changes |
| `ReplicationCredentialController` | `HubCredentialStore`, `ReplicationClient`, `ReplicationStatus`, `OfflineOrganizerAuth` | Hub-side organizer surface |
| `HubCredentialStore` | `HubDbContext`, `ISecretProtector` | Scoped |
| `DpapiSecretProtector` | `System.Security.Cryptography.ProtectedData` | **Registered by the host only** — the library never references it |
| `HttpCloudReplicationTransport` | `HttpClient`, `HubCredentialStore`, `ReplicationFailureClassifier`, `ReplicationCircuitBreaker`, `ReplicationStatus`, `ReplicationMetrics`, `EventEnvelopeMapper` | Singleton |
| `ReplicationClient` *(amended)* | `IServiceScopeFactory` → scoped `IEventStore`; `IReplicationProtocol`, `ICloudReplicationTransport`, `ReplicationSignal`, `ReplicationStatus`, `ReplicationMetrics` | Singleton `BackgroundService` |
| `HubEventWriter` *(touched)* | `ReplicationSignal` | **One added call** — the only U4a change |
| `ReplicationSignal` | none | Singleton, bounded channel |
| `ReplicationCircuitBreaker` | none | Pure state machine |
| `ReplicationFailureClassifier` | none | Pure function — trivially testable, deliberately |

---

## 2. Communication patterns

| Boundary | Pattern | Direction |
|---|---|---|
| Hub → Cloud | HTTPS request/response, credential in a request header | **Hub-initiated only** |
| `HubEventWriter` → `ReplicationClient` | In-process bounded channel, non-blocking, drop-on-full | One-way |
| `ReplicationClient` → transport | Direct call inside a per-run scope | Synchronous |
| Client/transport → `ReplicationStatus` | Shared singleton mutation | One-way |
| Components → `ReplicationMetrics` | `Meter` instruments | One-way |
| Hub → collector | OTLP over the network | One-way, **online only** (U10-CON-2) |

**Drop-on-full is deliberate, not a shortcut.** The signal carries no data — it only says "something
was appended" — so a dropped signal costs at most one drain-timer interval. The alternative, blocking
an append until the replication channel has room, would let a cloud problem slow down the event, which
inverts the entire offline-first premise.

---

## 3. Data flow — event to cloud

```text
Device/organizer action
   -> HubEventWriter.Append  ──────────────► hub.db (authoritative)
              │
              └─ Signal() ─► ReplicationSignal
                                   │
                     ReplicationClient (signal | timer | close-out)
                                   │
                     IReplicationProtocol.NextBatchAsync
                                   │  batch above each device cursor
                     EventEnvelopeMapper.ToDto
                                   │
                     HttpCloudReplicationTransport
                          credential header + timeout + HTTPS check
                                   │
                     POST /api/ingest/batch
                                   │
                     HubCredentialAuthenticationHandler -> IngestCaller
                                   │
                     IngestService: scope check, idempotent append, projections
                                   │
                     ReplicationAckDto (accepted count + per-device HWMs)
                                   │
                     cursors advance ─► ReplicationStatus / ReplicationMetrics
```

The hub's own write path is complete at line 2. Everything after it is best-effort mirroring — which
is what makes an outage a non-event rather than an incident.

---

## 4. Cross-solution dependencies

| From | To | Why | Verdict |
|---|---|---|---|
| `admin/EventManager.Hub` | `shared/EventManager.Contracts`, `shared/EventManager.Sync` | DTOs and protocol | Existing, unchanged |
| `backend/EventManager.Api` | same shared packages | Existing, unchanged |
| `tests/EventManager.Integration.Tests` | **both** `admin/EventManager.Hub` and `backend/EventManager.Api` | The one credential-path end-to-end test (F4=B) | **New — the only production-to-production coupling in the repo, and it is test-only** |

`EventManager.Integration.slnx` (CL-2=A) is a sixth solution whose sole purpose is to contain that
coupling and make it run. Without it the project would belong to no solution, and since Build-and-Test
drives `dotnet test` per solution, the test would never execute — which would quietly defeat the
reason F4=B added it.

**No production code in `admin/` references `backend/` or vice versa.** That separation is preserved.

---

## 5. Impact on existing components

| Component | Change | Blast radius |
|---|---|---|
| `IngestService` | Caller parameter type | Callers: `EventIngestController` and its tests |
| `EventIngestController` | Second scheme, new GET, rate limit, body cap | Route-local |
| `Program.cs` (cloud) | Register scheme + `"ingest"` policy | Composition only |
| `AppDbContext` | `HubCredentials` table + migration | Additive |
| `ReplicationClient` | **Substantial** — see U10-CON-6 amendment | 17 admin tests are the gate |
| `HubEventWriter` | One `Signal()` call | Minimal, but it is U4a code |
| `HubDbContext` | `HubCredentialRow` | Additive |
| `Program.cs` (hub) | Register transport, client, protector, status, metrics | Composition only |
| `StoreBackedReplicationTransport` | **None** | Retained for tests |
| `shared/` | **None** | Frozen |
