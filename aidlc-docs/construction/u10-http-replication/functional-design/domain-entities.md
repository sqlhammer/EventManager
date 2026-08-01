# U10 — Domain Entities

**Unit**: U10 HTTP Replication Adapter · **Stage**: Functional Design
Technology-agnostic. Persistence mapping is Code Generation's concern.

---

## 1. Cloud entities

### E-1 · `HubCredential` (new, persisted)

The cloud's record of one hub's identity for one event.

| Attribute | Type | Notes |
|---|---|---|
| `CredentialId` | identifier | Snowflake, consistent with the rest of the system |
| `EventScopeId` | identifier | The single event this credential may act on. Immutable after issue |
| `KeyHash` | string | Salted hash of the key. **The key itself is never persisted** |
| `Label` | string | Human identification only ("main hub", "spare laptop"). Required, bounded length, carries no authority |
| `IssuedByAccountId` | identifier | The organizer who issued it — audit, not authorization |
| `IssuedAt` | instant | |
| `ExpiresAt` | instant | Derived: `Event.Date` + grace (FD-Q1=C + CL-B=D). Never caller-supplied |
| `RevokedAt` | instant, optional | Set once; never cleared |

**Derived state** (not stored — computed, so it can never drift from the timestamps):

```text
Revoked   if RevokedAt is set
Expired   else if now >= ExpiresAt
Active    otherwise
```

**Invariants**
- At most **3** credentials in state `Active` per event (FD-Q2). Expired and revoked ones do not occupy a slot.
- `EventScopeId` never changes. Re-pointing a hub means issuing a new credential.
- Revocation is one-way. There is no un-revoke — re-issue instead.

### E-2 · `EventRecord` (existing, **amended**)

Gains one attribute:

| Attribute | Type | Notes |
|---|---|---|
| `IngestedByCredentialId` | identifier, **optional** | The credential that first delivered this event |

**Optional is required, not a convenience.** Events appended by the cloud itself — registration,
division configuration, results — have no ingesting credential at all, and every row that exists
today predates the attribute. Set once at insert, only on the ingest path (FD-Q7=B).

### E-3 · `IngestCaller` (value type)

The principal that presented a batch. Exactly one of:

- **Account caller** — `AccountId`. Authorizes as today, via `OrganizerAction.ManageRoster` on every scope in the batch.
- **Hub-credential caller** — `CredentialId` + `EventScopeId`. Authorizes only against its own bound scope.

Deliberately a closed set of two. A hub is not a person and is not modelled as one.

### E-4 · Cloud value types

| Type | Shape | Note |
|---|---|---|
| `IssuedHubCredential` | `CredentialId`, `Key`, `ExpiresAt`, `EventScopeId` | **The only place `Key` is ever populated.** Returned once, at issue |
| `HubCredentialPrincipal` | `CredentialId`, `EventScopeId` | Produced by authentication |
| `HubCredentialSummary` | `CredentialId`, `Label`, `IssuedAt`, `ExpiresAt`, `RevokedAt`, derived `State` | Listing shape — **no key material** |

---

## 2. Hub entities

### E-5 · `HubCloudCredential` (new, persisted)

The hub's local custody. **At most one row exists** (FD-Q8=B).

| Attribute | Type | Notes |
|---|---|---|
| `ProtectedKey` | bytes | Protected before write; unprotected only in memory, only at use |
| `CloudBaseUrl` | string | Validated as HTTPS unless the development override is enabled |
| `InstalledAt` | instant | |

**Invariants**
- Installing while a row exists is **refused**; clearing is a separate, explicit action (FD-Q8=B).
- The plaintext key is never returned by any read path. Callers may learn *that* a credential exists, never *what* it is.

### E-6 · Replication cursors — deliberately **not** an entity

Per-device cloud high-water marks live in memory and are seeded from the cloud at startup
(D-U10-06). They are **not persisted on the hub**, because the cloud is the authority on what it has
received; a persisted local copy could disagree with reality after a cloud-side restore, and would be
the kind of second source of truth this system avoids elsewhere. The cost of not persisting is one
cursor fetch per hub start.

### E-7 · Hub value types

| Type | Shape | Note |
|---|---|---|
| `ReplicationFailure` | `Kind` ∈ {TransientConnection, TransientResponse, Throttled, Permanent}, optional `RetryAfter` | Output of classification |
| `ReplicationStatusSnapshot` | `LastSuccessAt?`, `PendingEvents`, `LagSeconds?`, `ConsecutiveFailures`, `CircuitState`, `CredentialInstalled`, `ExpiryWarning?` | What `/health` and the status route expose. Contains **no key material** |
| `ReplicationResult` | *(existing, unchanged)* `Attempted`, `EventsReplicated` | |
| `CompletenessReport` | *(existing, unchanged)* `IsComplete`, `LocalEventCount`, `ReplicatedEventCount` | |

---

## 3. Relationships

```text
Event (1) ──< HubCredential (0..3 active)
                    │ issued by
                    ▼
              Account (organizer)

HubCredential (0..1) ──< EventRecord.IngestedByCredentialId  (optional; ingest path only)

Hub  ──holds──  HubCloudCredential (0..1)  ──refers to──  a HubCredential in the cloud
                                                          (by key, not by identifier)
```

The hub never learns a credential's `CredentialId` or `EventScopeId` — it holds only the opaque key.
That is precisely why FD-Q8=C was withdrawn at CL-A: the hub had no way to evaluate which event a
credential belonged to.

---

## 4. Entities explicitly unchanged

`EventEnvelope`, `ReplicationBatchDto`, `ReplicationAckDto`, `AccountRow`, `OrganizerRow`,
`EventRow`, `DeviceRecord`, `ReadinessRecord`, and every projection row. This unit adds one cloud
entity, one hub entity, and one optional attribute — nothing else in the domain moves.
