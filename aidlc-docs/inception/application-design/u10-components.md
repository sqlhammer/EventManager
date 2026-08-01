# U10 — Components

**Unit**: U10 HTTP Replication Adapter · **Stage**: Application Design
**Decisions**: AD-Q1..Q9 = A, A, A, B, C, A, A, A, C · CL-1=A · CL-2=A

Component boundaries and responsibilities only. Business rules belong to Functional Design.

---

## 1. Cloud components — `backend/EventManager.Api`

### C-U10-1 · `HubCredential` (persistence entity)
**Purpose**: the cloud's record of a hub's identity for one event.
**Responsibilities**: hold the credential's scope, lifetime, and revocation state; hold a **hash** of the key, never the key.
**Interface**: EF entity on `AppDbContext`; new migration `HubCredentials`. Additive — no existing table altered.
**Serves**: U10-FR-2, US-801.

### C-U10-2 · `HubCredentialService`
**Purpose**: the credential lifecycle — issue, authenticate, revoke, list.
**Responsibilities**: generate a high-entropy key and return it **once**; store only its hash; resolve a presented key to an active credential; enforce expiry and revocation; refuse issuance to an organizer without rights on the target event.
**Interface**: called by `HubCredentialController` (issue/revoke/list) and by `HubCredentialAuthenticationHandler` (authenticate).
**Serves**: U10-FR-2, U10-FR-4; US-801, US-808.
**Security note**: this is the unit's security-critical module, isolated per SECURITY-11.

### C-U10-3 · `HubCredentialAuthenticationHandler`
**Purpose**: make a hub a first-class authenticated principal.
**Responsibilities**: implement the `"HubCredential"` authentication scheme; read the presented key from its request header; delegate validation to `HubCredentialService`; emit a principal carrying the credential id and its bound event scope. Never logs the key.
**Interface**: `AuthenticationHandler<AuthenticationSchemeOptions>`, registered alongside the existing JWT bearer scheme (`Program.cs:89`).
**Serves**: U10-FR-3, U10-FR-4; US-809.

### C-U10-4 · `HubCredentialController`
**Purpose**: the organizer's cloud-side credential surface.
**Responsibilities**: issue, list, and revoke credentials for an event. Authenticated with the **existing JWT scheme** — an organizer is a person here — and authorized through the existing `EventAuthorizer`.
**Interface**: `POST/GET /api/events/{eventId}/hub-credentials`, `DELETE /api/events/{eventId}/hub-credentials/{id}`.
**Serves**: U10-FR-2, U10-FR-4; US-801, US-808.

### C-U10-5 · `IngestCaller` (principal abstraction)
**Purpose**: let ingest authorize two different kinds of caller without conflating them (AD-Q3=A).
**Responsibilities**: represent either an **account** caller (authorized via `OrganizerAction.ManageRoster`, as today) or a **hub-credential** caller (authorized by its own bound event scope, and by nothing else).
**Interface**: a discriminated caller type resolved from `HttpContext` by `CurrentCaller`; replaces the bare `long callerAccountId` parameter on `IngestService`.
**Serves**: U10-FR-3; US-809.
**Why not map a hub credential to its issuing account**: cloud audit would attribute hub writes to a person who was not present, and the credential's reach would track that organizer's role changes rather than its own event scope.

### C-U10-6 · `IngestCursorQuery`
**Purpose**: tell a restarting hub where the cloud actually is.
**Responsibilities**: return per-device high-water marks for one event scope, computed with the same gap-free rule `IngestService` already uses.
**Interface**: `GET /api/ingest/high-water-marks` on `EventIngestController`, accepting the `"HubCredential"` scheme.
**Serves**: U10-FR-12; US-805.

### C-U10-7 · Ingest hardening (configuration, not a class)
**Purpose**: keep ingest available under abuse.
**Responsibilities**: a named `"ingest"` rate-limit policy and an explicit request-body size limit on the ingest routes.
**Interface**: extends the **existing** `AddRateLimiter` registration (`Program.cs:106-114`), which already defines `login` and `registration` policies — a new policy, not a new dependency.
**Serves**: U10-FR-15; US-810.

---

## 2. Hub components — `admin/EventManager.Hub`

### C-U10-8 · `ISecretProtector` / `DpapiSecretProtector`
**Purpose**: keep a stored credential useless on any other machine.
**Responsibilities**: protect and unprotect a byte payload. The interface keeps `HubCredentialStore` free of any Windows dependency, and only the composition root names DPAPI.

**Corrected 2026-08-01 (Code Generation C-2)**: this originally claimed the library "stays platform-neutral" because the implementation is registered by the composition root. That was too strong — `admin/EventManager.Hub` is a **single project** that is both library and host, so there is no separate host project and the DPAPI package reference lands in the same `.csproj`. The accurate and narrower claim: `System.Security.Cryptography.ProtectedData` builds on every platform (it throws only at runtime, off Windows), so the project still compiles anywhere; the store carries no Windows dependency; tests substitute a pass-through; and a future library/host split becomes a project-file change rather than a refactor (U10-CON-1).
**Interface**: `Protect(byte[]) → byte[]`, `Unprotect(byte[]) → byte[]`. A pass-through implementation exists for tests.
**Serves**: U10-FR-5, D-U10-02; US-802.

### C-U10-9 · `HubCredentialStore`
**Purpose**: the hub's local custody of its credential.
**Responsibilities**: persist exactly one credential (protected) plus the cloud base URL in `hub.db`; load it at startup; clear it. Never returns the key to a caller that only needs to know whether one exists.
**Interface**: `SetAsync`, `TryGetAsync`, `ExistsAsync`, `ClearAsync`. New `HubCredentialRow` on `HubDbContext`.
**Serves**: U10-FR-5; US-802.

### C-U10-10 · `ReplicationCredentialController`
**Purpose**: how a credential reaches a hub that has no UI — **this closes U10-CON-5**.
**Responsibilities**: accept a credential and cloud base URL from an authenticated organizer and hand them to `HubCredentialStore`; report whether one is installed (without echoing it); trigger the close-out flush.
**Interface**: `POST /api/replication/credential`, `DELETE /api/replication/credential`, `GET /api/replication/status`, `POST /api/replication/close-out`, on the hub's existing controller surface behind `OfflineOrganizerAuth`. *(The `DELETE` route was added 2026-07-27 by Functional Design FD-Q8=B — install now refuses against an occupied slot, so clearing must be an explicit action.)*
**Serves**: U10-FR-5, U10-FR-11; US-802, US-807.

### C-U10-11 · `HttpCloudReplicationTransport`
**Purpose**: the actual adapter — the deferred seam this unit exists to fill.
**Responsibilities**: POST a `ReplicationBatchDto` to the cloud ingest route and return the `ReplicationAckDto`; fetch cloud cursors; attach the credential to every request; enforce the explicit timeout; refuse a non-HTTPS base URL unless the development override is set; report `IsOnline` from the circuit breaker.
**Interface**: implements the existing `ICloudReplicationTransport` **unchanged** — plus a cursor-fetch method used only at startup.
**Serves**: U10-FR-1, U10-FR-12, U10-FR-14; US-803, US-805, US-809.

### C-U10-12 · `ReplicationFailureClassifier`
**Purpose**: decide what is worth retrying.
**Responsibilities**: map a transport outcome to transient or permanent per the US-804 table; extract the wait a throttling response asks for.
**Interface**: pure function over an HTTP outcome, producing a typed transient or permanent failure.
**Serves**: U10-FR-6, U10-FR-8; US-804.

### C-U10-13 · `ReplicationCircuitBreaker`
**Purpose**: stop hammering a dead link.
**Responsibilities**: count consecutive connection failures, open after the threshold, permit a trial request after the cool-down, close on success. Defaults 3 failures / 60s, configurable (D-U10-04).
**Interface**: `IsClosed`, `RecordSuccess()`, `RecordConnectionFailure()`; drives `HttpCloudReplicationTransport.IsOnline`.
**Serves**: U10-FR-9; US-804.

### C-U10-14 · `ReplicationSignal` (append channel)
**Purpose**: make replication genuinely append-driven rather than poll-driven (AD-Q5=C).
**Responsibilities**: a bounded, drop-on-full in-process channel that `HubEventWriter` posts to after a successful append and `ReplicationClient` consumes with debouncing. Dropping is safe and deliberate: the signal carries no data, and the drain timer is the backstop.
**Interface**: `Signal()`, `WaitForSignalAsync(ct)`.
**Serves**: U10-FR-10; US-803.
**Touches existing code**: `HubEventWriter` (U4a) gains one post call.

### C-U10-15 · `ReplicationClient` (**amended**, merged U7 code)
**Purpose**: drive replication and own its schedule (AD-Q4=B).
**Responsibilities**: unchanged — compute the next batch above each device's cloud high-water mark, send, advance cursors, verify completeness. **Added** — retry only transient failures; consume `ReplicationSignal` with debouncing; run a drain timer; expose a close-out flush; seed cursors from the cloud at startup; run as a `BackgroundService`; obtain a scoped `IEventStore` through `IServiceScopeFactory`, one scope per run (CL-1=A).
**Serves**: U10-FR-7, U10-FR-10, U10-FR-11, U10-FR-12, U10-FR-19; US-803, US-804, US-805, US-807.
**Risk**: the only edit to merged U7 code, and it is no longer small — see the U10-CON-6 amendment. The 17 existing admin tests are the gate.

### C-U10-16 · `ReplicationStatus`
**Purpose**: answer "is the cloud current?" at a venue with no internet (AD-Q7=A).
**Responsibilities**: hold last successful replication time, pending backlog, consecutive failures, and circuit state, updated in-process. Deliberately **not** dependent on reaching the cloud.
**Interface**: read by the hub `/health` endpoint and by `GET /api/replication/status`.
**Serves**: U10-FR-17; US-806.

### C-U10-17 · `ReplicationMetrics`
**Purpose**: instrumentation without coupling components to an exporter (AD-Q8=A).
**Responsibilities**: own a `System.Diagnostics.Metrics.Meter` and its instruments — events replicated, batches sent, failures by class, replication lag, backlog depth. Carries **no credential value in any label** (U10-NFR-5).
**Interface**: `Meter`-based counters and gauges; OpenTelemetry export configured only at the composition root.
**Serves**: U10-FR-18; US-806.

---

## 3. Infrastructure component

### C-U10-18 · OTLP collector (Compose service)
**Purpose**: a destination for hub metrics (F3=B).
**Responsibilities**: receive OTLP from the hub and expose it for inspection.
**Interface**: a new service in the cloud Compose stack; its network exposure, access logging, and image pinning are decided at **Infrastructure Design** (SECURITY-07, SECURITY-02, SECURITY-10).
**Serves**: U10-FR-18.
**Known limitation**: unreachable during an outage — U10-CON-2. `ReplicationStatus` (C-U10-16), not this, is the venue-visible signal.

---

## 4. Components explicitly NOT changed

| Component | Why |
|---|---|
| `shared/EventManager.Contracts` | `ReplicationBatchDto`/`ReplicationAckDto` are the wire contract and stay frozen (D-U10-15). |
| `shared/EventManager.Sync` — `IReplicationProtocol` | Batch computation is already correct; U10 changes transport and schedule, not the protocol. |
| `StoreBackedReplicationTransport` | Retained unchanged as the in-process implementation for tests and loopback. |
| `EventAuthorizer`, `ReadAuthorizer` | Account-based authorization is untouched; hub credentials are a parallel path. |
| `OrganizerAction` enum | Not extended — the same restraint U9 applied at U9-CON-1. A hub credential's permission is its event scope, not an organizer action. |
| U4a `PairingService` / `DeviceRegistry` | Spoke→hub pairing is a different concern; AD-Q1 rejected reusing it for hub→cloud. |
