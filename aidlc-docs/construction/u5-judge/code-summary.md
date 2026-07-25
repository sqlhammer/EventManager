# U5 Judge App — Code & Verification Summary

**Stage**: CONSTRUCTION → Code Generation (fast-tracked) · **Unit**: U5 Judge · **Date**: 2026-07-25
Branch `unit/u5-judge` · Code under CS-1 (no ternaries).

## What shipped
- **`judge/EventManager.Judge.Core`** (net10.0) — testable app logic. Consumes U1 (`IIdGenerator`, scoring types) + U2 (`LocalEventQueue`, Contracts).
- **`judge/EventManager.Judge`** — **MAUI Windows head that COMPILES** (`net10.0-windows10.0.19041.0`). References the core; DI composition root in `MauiProgram`. **Build succeeded** (verified).
- **`judge/tests/EventManager.Judge.Core.Tests`** — xUnit; **6 tests passing**.

## Core components
| Component | Responsibility | Stories |
|---|---|---|
| `SpokeEventLog` | Durable-before-ack write path (mint id + contiguous per-device sequence → persist BEFORE ack) | NFR-1.1 |
| `ScoreCaptureService` | Capture point-sparring / forms scores, persisted durably; hub owns the authoritative outcome | US-402/403 |
| `MatQueueViewModel` | Assigned-mat match queue; advances on hub push | US-401 |
| `CrossMatViewModel` | **Read-only** cross-mat view — no write path (enforced by type) | US-410 |
| `FocusModeState` | Match focus/lock toggle | US-411 |
| `InMemoryEventStore` | Default `IEventStore`; on-device SQLite/SQLCipher is a host seam | — |

## MAUI head status
Windows head compiles here. **Android head can't build** (no JDK / Android SDK); **iOS/Mac can't** (need a Mac). The other TFMs are a one-line `<TargetFramework>` addition once the toolchains exist — the MAUI UI code is platform-agnostic.

## Tests (6)
Durable-before-ack point-sparring capture; contiguous per-device sequences; queued scores drain after hub ack; mat queue advances; cross-mat view is read-only (reflection asserts no write method); focus lock/unlock.

## Deferred seams
MAUI UI interactions (visual); concrete SignalR/WSS transport + mDNS (host wiring); on-device SQLite/SQLCipher store; Android/iOS/Mac heads (toolchain).

## Verify
```bash
dotnet test  judge/tests/EventManager.Judge.Core.Tests/EventManager.Judge.Core.Tests.csproj   # 6 passed
dotnet build judge/EventManager.Judge/EventManager.Judge.csproj                                # Windows head: Build succeeded
```
