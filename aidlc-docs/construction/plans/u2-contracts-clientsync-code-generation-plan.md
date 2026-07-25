# Code Generation Plan — U2 Contracts & ClientSync (fast-tracked)

**Branch**: `unit/u2-contracts-clientsync` · **Date**: 2026-07-25

Add two libraries + two test projects to the existing `shared/EventManager.Shared.slnx`.

- [x] Step 1 — Scaffold `EventManager.Contracts`, `EventManager.ClientSync`, and their xUnit test projects; add to solution; references (Contracts→Sync; ClientSync→Sync,Contracts; tests accordingly); packages (FluentValidation on Contracts; FsCheck.Xunit on tests)
- [x] Step 2 — Contracts: DTO records + `EventEnvelopeMapper` + FluentValidation validators
- [x] Step 3 — ClientSync: transport seams (`ISyncTransport`, `IHubDiscovery`), `LocalEventQueue`, `SyncClient`, `ReconnectSupervisor`, `HubPushConsumer`, `PairingClient`, state types
- [x] Step 4 — Contracts tests: envelope round-trip + validator rules
- [x] Step 5 — ClientSync tests: durable-before-ack, idempotent replay (fake transport), reconnect RunOnce, push idempotence, sync-status honesty
- [x] Step 6 — Build + test the full shared solution
- [x] Step 7 — Docs: code-summary.md + README update
- [x] Step 8 — End-of-unit: update architecture-overview.md (U2 as-built) + author user-testing-guide.md
- [ ] Not applicable: API/repository/frontend/migrations/deployment (libraries)
