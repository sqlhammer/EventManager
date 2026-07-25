# U2 — Tech Stack Decisions (fast-tracked, AI-recommended)

**Branch**: `unit/u2-contracts-clientsync` · **Date**: 2026-07-25

| # | Decision | Rationale |
|---|---|---|
| U2-TSD-1 | **FluentValidation** for Contracts DTO validators | expressive, testable; NFR-2.4 lists it first; runs before the write path |
| U2-TSD-2 | **System.Text.Json** for DTO (de)serialization | consistent with U1 |
| U2-TSD-3 | **Lightweight typed .NET event** for push notifications (no System.Reactive) | Q3=A intent without a reactive dependency; keeps deps minimal |
| U2-TSD-4 | **Transport seams** `ISyncTransport` + `IHubDiscovery` in ClientSync; concrete SignalR/WSS impl deferred to app wiring | makes ClientSync unit-testable with fakes; real transport is integration-tested |
| U2-TSD-5 | Durability via U1 **`IEventStore`** (Q2=A); no new persistence in U2 | one storage seam |
| U2-TSD-6 | Coverage gate **90% on ClientSync core**; validators covered | resilience-critical |
| U2-TSD-7 | Backoff: initial 1s, max 30s, ×2 | bounded reconnect (NFR-3.8) |

## Dependencies (supply chain, NFR-2.9)
| Dependency | Project | Scope | License |
|---|---|---|---|
| FluentValidation | Contracts | runtime | Apache-2.0 |
| System.Text.Json | both | runtime (BCL) | MIT |
| EventManager.Sync / .Domain | via project ref | runtime | (in-repo, U1) |
| FsCheck.Xunit / xUnit | tests | test | BSD / Apache-2.0 |

ClientSync adds **no** new third-party runtime dependency (only project refs to U1 + Contracts).
