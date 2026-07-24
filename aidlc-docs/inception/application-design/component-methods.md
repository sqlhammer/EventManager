# Application Design — Component Methods

**Stage**: INCEPTION - Application Design
**Date**: 2026-07-24
**Altitude**: Method signatures + purpose + I/O types. **Detailed business rules are defined later in Functional Design (per-unit, CONSTRUCTION).** Signatures below are indicative interface contracts, not final code.

Conventions: `Snowflake` = 64-bit ID (stored `BIGINT`/`INTEGER`). Async methods return `Task`/`Task<T>`. Types reference `EventManager.Domain` / `EventManager.Contracts`.

---

## `EventManager.Domain`

### `IBracketEngine`
| Method | Purpose | In → Out |
|---|---|---|
| `GenerateSingleElimination(divisionId, athletes)` | Build single-elim bracket with byes | `(Snowflake, IReadOnlyList<Seed>)` → `Bracket` |
| `GenerateRoundRobin(divisionId, athletes)` | Build round-robin schedule | `(Snowflake, IReadOnlyList<Seed>)` → `Bracket` |
| `Advance(bracket, matchOutcome)` | Produce next bracket state from an outcome | `(Bracket, MatchOutcome)` → `Bracket` |

### `ISeedingEngine`
| Method | Purpose | In → Out |
|---|---|---|
| `Seed(athletes, options)` | Random baseline + academy separation | `(IReadOnlyList<Registration>, SeedingOptions)` → `IReadOnlyList<Seed>` |

### `IScoringEngine` / `IRuleset`
| Method | Purpose | In → Out |
|---|---|---|
| `Score(ruleset, inputs)` | Compute a match/forms outcome | `(IRuleset, ScoreInputs)` → `MatchOutcome` |
| `IRuleset.Evaluate(entries)` | Ruleset-specific evaluation (point-sparring, forms) | `IReadOnlyList<ScoreEntry>` → `RulesetResult` |

### `IWeighInPolicyEvaluator`
| Method | Purpose | In → Out |
|---|---|---|
| `Evaluate(weighIn, division, policy)` | Propose outcome (pass / DQ / move / tolerance) | `(WeighIn, Division, WeighInPolicy)` → `WeighInOutcomeProposal` |

### `IRoleAuthorizationPolicy`
| Method | Purpose | In → Out |
|---|---|---|
| `IsPermitted(assignment, action)` | Pure RBAC decision incl. Full-Admin-only set | `(OrganizerRoleAssignment, OrganizerAction)` → `bool` |

---

## `EventManager.Sync`

### `IIdGenerator` (SnowflakeIdGenerator)
| Method | Purpose | In → Out |
|---|---|---|
| `NextId()` | Next monotonic Snowflake for this worker | `()` → `Snowflake` |
| `Configure(workerId, epoch)` | Bind worker ID + custom epoch | `(int, DateTimeOffset)` → `void` |

### `IEventStore`
| Method | Purpose | In → Out |
|---|---|---|
| `AppendIfNotExistsAsync(evt)` | Idempotent append (dedupe on `EventId`) | `TournamentEvent` → `Task<bool>` |
| `ReadStreamAsync(deviceId, fromSeq)` | Read a device stream from a sequence | `(Snowflake, long)` → `Task<IReadOnlyList<TournamentEvent>>` |
| `HighWaterMarkAsync(deviceId)` | Last contiguous sequence for a device | `Snowflake` → `Task<long>` |
| `ReadAllAsync(fromEventId)` | Ordered read for projection rebuild | `Snowflake?` → `IAsyncEnumerable<TournamentEvent>` |

### `IReplayEngine`
| Method | Purpose | In → Out |
|---|---|---|
| `Apply(state, evt)` | Idempotent fold of one event | `(TState, TournamentEvent)` → `TState` |
| `Rebuild(events)` | Fold a full stream into state | `IEnumerable<TournamentEvent>` → `TState` |

### `IProjectionHost` / `IProjection<TState>`
| Method | Purpose | In → Out |
|---|---|---|
| `RebuildAsync()` | Rebuild all projections from the log on startup (Q3) | `()` → `Task` |
| `Dispatch(evt)` | Incrementally update projections | `TournamentEvent` → `void` |
| `Get<TState>()` | Read current projected state | `()` → `TState` |

### `IReplicationProtocol`
| Method | Purpose | In → Out |
|---|---|---|
| `NextBatchAsync(peerHighWaterMarks)` | Compute sequence-ordered batch to send | `IReadOnlyDictionary<Snowflake,long>` → `Task<IReadOnlyList<TournamentEvent>>` |
| `DetectGaps(deviceId)` | Identify missing sequence ranges | `Snowflake` → `IReadOnlyList<SeqRange>` |

### `IWorkerIdRegistry`
| Method | Purpose | In → Out |
|---|---|---|
| `AssignWorkerId(deviceId)` | Reserve a unique worker ID within the event | `Snowflake` → `int` |
| `Release(deviceId)` | Free a worker ID on revoke | `Snowflake` → `void` |

---

## `EventManager.ClientSync`

### `ILocalEventQueue`
| Method | Purpose | In → Out |
|---|---|---|
| `EnqueueDurableAsync(evt)` | Persist locally BEFORE ack (NFR-1.1) | `TournamentEvent` → `Task` |
| `PendingAsync()` | Unsent events in order | `()` → `Task<IReadOnlyList<TournamentEvent>>` |
| `MarkAcknowledgedAsync(upToSeq)` | Drop acked events | `long` → `Task` |

### `ISyncClient`
| Method | Purpose | In → Out |
|---|---|---|
| `ConnectAsync(credential)` | Open WSS session to hub | `DeviceCredential` → `Task` |
| `ReplayPendingAsync()` | Send queued events idempotently | `()` → `Task<SyncResult>` |
| `Status` | Current sync status (queued count, connected) | property → `SyncStatus` |

### `IReconnectSupervisor` / `IHubPushConsumer` / `IPairingClient`
| Method | Purpose | In → Out |
|---|---|---|
| `ReconnectSupervisor.Start()` | Begin auto-reconnect loop (US-507) | `()` → `void` |
| `HubPushConsumer.OnUpdate(handler)` | Subscribe to SignalR pushes | `Action<PushMessage>` → `IDisposable` |
| `PairingClient.DiscoverAsync()` | mDNS + fallback discovery | `()` → `Task<IReadOnlyList<HubEndpoint>>` |
| `PairingClient.PairAsync(qrPayload)` | Redeem token, pin cert, get credential | `PairingPayload` → `Task<DeviceCredential>` |

---

## `backend/` — cloud-backend (selected controller/service methods)

| Component.Method | Purpose | In → Out |
|---|---|---|
| `AccountController.Register` | Create account (organizer/coach/registrant) | `RegisterRequest` → `AccountResponse` |
| `AccountController.Login` | Authenticate, issue JWT (+ MFA) | `LoginRequest` → `TokenResponse` |
| `EventController.Create` | Create event; creator → Full Admin | `CreateEventRequest` → `EventResponse` |
| `OrganizerController.AddOrganizer` | Invite/direct-add co-organizer (FR-2.7) | `AddOrganizerRequest` → `OrganizerResponse` |
| `OrganizerController.ChangeRole` | Elevate/demote; Full-Admin-only (FR-2.8) | `ChangeRoleRequest` → `OrganizerResponse` |
| `RegistrationController.Register` | Register athlete(s); assign divisions | `RegistrationRequest` → `RegistrationResponse` |
| `EventIngestController.IngestBatch` | Idempotent replicated-event ingest (US-504) | `IReadOnlyList<TournamentEvent>` → `IngestResult` |
| `ResultsController.GetForAthlete` | Results/history read model | `athleteId` → `ResultsResponse` |

---

## `admin/` — admin-hub (selected service methods)

| Component.Method | Purpose | In → Out |
|---|---|---|
| `EventDownloadService.DownloadAsync` | Pull full event to local store; readiness gate | `eventId` → `Task<DownloadResult>` |
| `HubServer.StartAsync` | Start Kestrel + WSS + SignalR + mDNS | `HubStartOptions` → `Task` |
| `PairingService.IssuePairing` | Create QR + one-time token + role + worker ID | `PairingRequest` → `PairingPayload` |
| `PairingService.RevokeDevice` | Revoke credential; free worker ID (US-508) | `deviceId` → `Task` |
| `OrganizerAuthService.AuthenticateAsync` | Offline organizer authN | `HubLoginRequest` → `Task<HubSession>` |
| `OrganizerAuthService.Authorize` | Hub-side RBAC check (Q5b) | `(HubSession, OrganizerAction)` → `bool` |
| `BracketService.Generate` | Generate/regenerate a division bracket | `divisionId` → `Task<Bracket>` |
| `BracketService.ApplyOutcome` | Advance bracket from a scored match | `MatchOutcome` → `Task<Bracket>` |
| `WeighInPolicyService.Resolve` | Apply organizer resolution; maybe regenerate | `WeighInResolution` → `Task` |
| `ReplicationClient.ReplicateAsync` | Push pending events to cloud (retry/backoff) | `()` → `Task<ReplicationResult>` |
| `BackupService.ExportAsync` | Produce encrypted log snapshot | `BackupOptions` → `Task<BackupFile>` |
| `RecoveryService.RestoreAsync` | Rebuild hub from replica/backup by replay | `RestoreSource` → `Task<RestoreResult>` |

---

## `judge/` & `checkin/` (selected view-model methods)

| Component.Method | Purpose | In → Out |
|---|---|---|
| `ScoringViewModel.SubmitOutcomeAsync` | Durable-before-ack score submit (US-402/403) | `ScoreInputs` → `Task` |
| `CrossMatViewModel.LoadMatAsync` | Read-only other-mat queue when connected (US-410) | `matId` → `Task` |
| `FocusModeController.Enable/Disable` | Toggle single-match lock (US-411) | `()` → `void` |
| `CheckInViewModel.MarkPresentAsync` | Append check-in event (US-306) | `athleteId` → `Task` |
| `WeighInViewModel.RecordAsync` | Record weight + range feedback (US-307) | `WeighInInput` → `Task<WeighInFeedback>` |
| `RecommendationController.Attach` | Attach non-binding policy recommendation (D-25) | `(weighInId, recommendation)` → `Task` |
