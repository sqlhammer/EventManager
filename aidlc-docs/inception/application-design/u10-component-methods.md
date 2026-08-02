# U10 — Component Methods

**Unit**: U10 HTTP Replication Adapter · **Stage**: Application Design

Signatures, purpose, and input/output types. **Business rules are deliberately absent** — expiry
precedence, classification edge cases, breaker transitions, and completeness semantics are Functional
Design's job (`BR-REPL-*`). Signatures are indicative C#; exact naming settles at Code Generation.

---

## Cloud — `backend/EventManager.Api`

### `HubCredentialService`

| Method | Purpose | In → Out |
|---|---|---|
| `IssueAsync` | Create a credential for an event; return the key **once**, store only its hash | `(long issuerAccountId, long eventScopeId, string label, DateTimeOffset expiresAt, CancellationToken)` → `ErrorOr<IssuedHubCredential>` |
| `AuthenticateAsync` | Resolve a presented key to an active credential | `(string presentedKey, CancellationToken)` → `HubCredentialPrincipal?` |
| `RevokeAsync` | Revoke immediately | `(long issuerAccountId, long eventScopeId, long credentialId, CancellationToken)` → `ErrorOr<Success>` |
| `ListAsync` | Credentials for an event, **without** key material | `(long callerAccountId, long eventScopeId, CancellationToken)` → `ErrorOr<IReadOnlyList<HubCredentialSummary>>` |

**Types**: `IssuedHubCredential(long CredentialId, string Key, DateTimeOffset ExpiresAt)` — the only place `Key` is ever populated. `HubCredentialPrincipal(long CredentialId, long EventScopeId)`. `HubCredentialSummary(long CredentialId, string Label, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)`.

### `HubCredentialAuthenticationHandler`

| Method | Purpose | In → Out |
|---|---|---|
| `HandleAuthenticateAsync` | Validate the presented key and build a principal | request context → `AuthenticateResult` |

Emits claims for credential id and bound event scope. Failure is a generic non-authenticated result carrying no detail about *why* (SECURITY-09, US-809).

### `HubCredentialController`

| Route | Purpose |
|---|---|
| `POST /api/events/{eventId}/hub-credentials` | Issue; body carries label and expiry; response carries the key once |
| `GET /api/events/{eventId}/hub-credentials` | List summaries |
| `DELETE /api/events/{eventId}/hub-credentials/{credentialId}` | Revoke |

All three use the **existing JWT scheme** — the caller is a person.

### `IngestService` (amended)

| Method | Change |
|---|---|
| `IngestAsync` | `(long callerAccountId, ...)` → `(IngestCaller caller, ReplicationBatchDto batch, CancellationToken)`. An account caller authorizes exactly as today; a hub-credential caller authorizes only against its bound scope. Return type unchanged: `ErrorOr<ReplicationAckDto>`. |
| `HighWaterMarksAsync` *(new)* | `(IngestCaller caller, long eventScopeId, CancellationToken)` → `ErrorOr<IReadOnlyDictionary<long, long>>` — reuses the existing gap-free rule |

### `EventIngestController` (amended)

| Route | Change |
|---|---|
| `POST /api/ingest/batch` | Accepts **both** schemes; resolves an `IngestCaller`; gains the `"ingest"` rate-limit policy and a body-size limit |
| `GET /api/ingest/high-water-marks` *(new)* | Returns cloud cursors for the caller's event scope |

---

## Hub — `admin/EventManager.Hub`

### `ISecretProtector`

| Method | In → Out |
|---|---|
| `Protect` | `byte[] plaintext` → `byte[] protectedBytes` |
| `Unprotect` | `byte[] protectedBytes` → `byte[] plaintext` |

`DpapiSecretProtector` (host-registered, Windows) and `PassthroughSecretProtector` (tests).

### `HubCredentialStore`

| Method | Purpose | In → Out |
|---|---|---|
| `SetAsync` | Store credential + base URL, protected | `(string key, string cloudBaseUrl, CancellationToken)` → `Task` |
| `TryGetAsync` | Load for use by the transport | `(CancellationToken)` → `Task<HubCloudCredential?>` |
| `ExistsAsync` | Answer "is one installed" **without** returning it | `(CancellationToken)` → `Task<bool>` |
| `ClearAsync` | Remove | `(CancellationToken)` → `Task` |

### `HttpCloudReplicationTransport` — implements `ICloudReplicationTransport`

| Method | Purpose | In → Out |
|---|---|---|
| `IsOnline` *(property, existing)* | Breaker state; also false when no credential is installed | → `bool` |
| `SendAsync` *(existing)* | POST a batch, return the ack | `(ReplicationBatchDto, CancellationToken)` → `Task<ReplicationAckDto>` |
| `GetHighWaterMarksAsync` *(new)* | Fetch cloud cursors for startup seeding | `(CancellationToken)` → `Task<IReadOnlyDictionary<long, long>>` |

Throws the typed transient/permanent failures produced by `ReplicationFailureClassifier`. Rejects a non-HTTPS base URL at construction unless the development override is set.

### `ReplicationFailureClassifier`

| Method | In → Out |
|---|---|
| `Classify` | `(HttpResponseMessage? response, Exception? exception)` → `ReplicationFailure(FailureKind Kind, TimeSpan? RetryAfter)` |

`FailureKind` ∈ { `TransientConnection`, `TransientResponse`, `Throttled`, `Permanent` }. `TransientConnection` is the only kind that advances the circuit breaker — a `500` means the cloud is reachable and unwell, which is a different situation from an unreachable venue link.

### `ReplicationCircuitBreaker`

| Method | In → Out |
|---|---|
| `IsClosed` | → `bool` |
| `RecordSuccess` | → `void` |
| `RecordConnectionFailure` | → `void` |

### `ReplicationSignal`

| Method | In → Out |
|---|---|
| `Signal` | → `void` (non-blocking; drops when full, by design) |
| `WaitForSignalAsync` | `(CancellationToken)` → `Task` |

### `ReplicationClient` (amended)

| Method | Status | Purpose |
|---|---|---|
| `ReplicateAsync` | existing | Drive batches until the backlog is empty |
| `VerifyCompletenessAsync` | existing | US-602 completeness report |
| `ExecuteAsync` | **new** | `BackgroundService` loop: seed cursors, then react to signal-or-timer |
| `SeedCursorsAsync` | **new** | Populate cursors from `GetHighWaterMarksAsync`; on failure, start anyway with empty cursors — re-sending is wasteful, never incorrect (US-805) |
| `FlushForCloseOutAsync` | **new** | Replicate to completion, then verify completeness | 
| `SendWithRetryAsync` | **amended** | Retry only transient failures; honour a throttling wait; propagate permanent failures immediately |

Construction changes from `(IEventStore local, IReplicationProtocol, ICloudReplicationTransport, int maxBatch, int maxAttempts)` to taking `IServiceScopeFactory` for the scoped store (CL-1=A), plus `ReplicationSignal`, `ReplicationStatus`, and options. **The existing direct-construction tests (`ResilienceTests.cs:56,117`) must keep compiling and passing** — Functional Design decides whether that means an overload, a defaulted parameter, or updating those two call sites.

### `ReplicationStatus`

| Member | Purpose |
|---|---|
| `LastSuccessAt` | `DateTimeOffset?` |
| `PendingEvents` | `long` |
| `ConsecutiveFailures` | `int` |
| `CircuitState` | open / closed |
| `CredentialInstalled` | `bool` |
| `RecordSuccess` / `RecordFailure` | update points for client and transport |

### `ReplicationCredentialController`

| Route | Purpose |
|---|---|
| `POST /api/replication/credential` | Install a credential + base URL (**closes U10-CON-5**) |
| `DELETE /api/replication/credential` | Clear the installed credential — **added 2026-07-27 by Functional Design FD-Q8=B**, which makes install refuse against an occupied slot and therefore requires an explicit clear step. `HubCredentialStore.ClearAsync` already existed; this adds the route, not the capability |
| `GET /api/replication/status` | Read `ReplicationStatus` — never echoes the credential |
| `POST /api/replication/close-out` | Trigger `FlushForCloseOutAsync` and return the completeness report |

Behind the hub's existing `OfflineOrganizerAuth`.

### `ReplicationMetrics`

Instruments: `replication.events.sent` (counter), `replication.batches` (counter), `replication.failures` (counter, tagged by failure kind), `replication.backlog` (gauge), `replication.lag.seconds` (gauge). No credential value appears in any tag or label.
