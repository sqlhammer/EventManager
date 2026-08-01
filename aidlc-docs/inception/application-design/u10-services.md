# U10 — Services and Orchestration

**Unit**: U10 HTTP Replication Adapter · **Stage**: Application Design

---

## 1. Service inventory

| Service | Side | Responsibility | Lifetime |
|---|---|---|---|
| `HubCredentialService` | Cloud | Credential lifecycle: issue, authenticate, revoke, list | Scoped |
| `IngestService` *(amended)* | Cloud | Idempotent, sequence-ordered append; now caller-type aware | Scoped |
| `HubCredentialStore` | Hub | Local custody of the credential | Scoped |
| `HttpCloudReplicationTransport` | Hub | HTTP conversation with the cloud | Singleton (owns `HttpClient` and breaker) |
| `ReplicationClient` *(amended)* | Hub | Drive replication **and** own its schedule | **Singleton `BackgroundService`** — takes `IServiceScopeFactory` (CL-1=A) |
| `ReplicationStatus` | Hub | In-process replication health | Singleton |
| `ReplicationMetrics` | Hub | Meter and instruments | Singleton |

**Lifetime is the sharp edge of this design.** `ReplicationClient` becomes a singleton while
`IEventStore`/`HubEventStore` and `HubDbContext` remain scoped (`Program.cs:16,40`). It therefore
resolves a store inside a per-run scope, never holds one. Getting this wrong does not fail at
startup — it fails intermittently under concurrent load, which is the worst way for it to fail.

---

## 2. Orchestration — four flows

### Flow 1 · Credential provisioning (US-801, US-802 — closes U10-CON-5)

```text
Organizer --(JWT)--> HubCredentialController.Issue
                       -> EventAuthorizer: rights on this event?
                       -> HubCredentialService.IssueAsync
                            -> generate key, store HASH only
                       <- key returned ONCE

Organizer --(hub OfflineOrganizerAuth)--> ReplicationCredentialController.Install
                       -> HubCredentialStore.SetAsync
                            -> ISecretProtector.Protect  (DPAPI)
                            -> hub.db row
                       -> ReplicationStatus.CredentialInstalled = true
```

Two authenticated hops with a human in between. The cloud never contacts the hub; the hub is not
addressable from the internet, which is why enrolment is organizer-mediated rather than automatic.

### Flow 2 · Steady-state replication (US-803, US-804)

```text
HubEventWriter.Append  --> ReplicationSignal.Signal()          (fire and forget)
                                   |
ReplicationClient (BackgroundService)
   waits on: signal OR drain timer OR close-out request
   -> IServiceScopeFactory.CreateScope() -> IEventStore
   -> IReplicationProtocol.NextBatchAsync(store, cursors, maxBatch)
   -> HttpCloudReplicationTransport.SendAsync
        -> breaker closed? credential present? HTTPS?
        -> POST /api/ingest/batch  (timeout enforced)
        -> ReplicationFailureClassifier.Classify
   -> transient  -> backoff (or the wait a throttle asks for) -> retry
   -> permanent  -> stop, surface distinctly, do NOT retry
   -> success    -> advance cursors from ack, update Status + Metrics
   -> repeat while a backlog remains
```

The drain timer is what makes the breaker recoverable and the 5-minute lag target reachable — an
append-only trigger would leave a backlog stranded whenever the log goes quiet.

### Flow 3 · Startup cursor seeding (US-805)

```text
ReplicationClient.ExecuteAsync
   -> transport.GetHighWaterMarksAsync
        -> success -> seed cursors, resume from the cloud's real position
        -> failure -> start with empty cursors and proceed
                      (re-sending is idempotent: wasteful, never incorrect)
```

Deliberately non-blocking: a hub must start at a venue with no internet.

### Flow 4 · Close-out (US-807)

```text
Organizer -> ReplicationCredentialController.CloseOut
   -> ReplicationClient.FlushForCloseOutAsync
        -> replicate until the backlog is empty
        -> VerifyCompletenessAsync
   <- CompletenessReport (IsComplete, local count, replicated count)
```

Close-out cannot rely on appends — by definition the log has gone quiet — which is precisely the gap
that made F2=C necessary.

---

## 3. Coordination and boundaries

- **Frozen contract**: `ReplicationBatchDto` / `ReplicationAckDto`. No `shared/` change.
- **Authorization split**: person-facing routes use JWT; machine-facing ingest routes accept the `"HubCredential"` scheme. `EventIngestController` accepts both so account-based ingest keeps working.
- **The hub is never a server to the cloud.** All traffic is hub → cloud. This is why revocation takes effect on the hub's *next attempt* rather than being pushed.
- **Failure isolation**: only `TransientConnection` advances the breaker. A `500` means the cloud is reachable and unwell — a different situation from an unreachable venue link, and conflating them would open the breaker on a problem that fast retry would ride out.
- **Degraded modes**, all non-fatal: no credential → hub runs, does not replicate, says so; breaker open → replication is a no-op until cool-down; cloud unreachable at startup → start anyway with empty cursors.
