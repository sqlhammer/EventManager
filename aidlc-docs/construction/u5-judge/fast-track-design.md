# U5 Judge App — Fast-Track Design

**Stage**: CONSTRUCTION (fast-tracked) · **Unit**: U5 Judge (`judge/`) · **Date**: 2026-07-25
**Branch**: `unit/u5-judge` · Code under CS-1 (no ternaries).

## Stories (5)
US-401 mat match queue · US-402 point-sparring scoring · US-403 forms/kata scoring · US-410 read-only cross-mat view · US-411 match focus/lock mode.

## Packaging decision (MAUI partially available)
`maui-windows` + `maui-android` workloads are installed, but **no JDK/Android SDK** (Android head can't compile) and **no Mac** (iOS/Mac heads can't compile). Verified a **Windows MAUI head builds**. So:
- **`judge/EventManager.Judge.Core`** (plain `net10.0`) — all testable app logic; consumes U1 (ScoringEngine) + U2 (ClientSync `LocalEventQueue`/`ISyncTransport`/`PairingClient`, Contracts). Headless, xUnit/FsCheck tested.
- **`judge/EventManager.Judge`** — MAUI **Windows head** (`net10.0-windows10.0.19041.0` only, so `dotnet build` stays green) referencing the core; thin UI shell. Android/iOS/Mac TFMs are a one-line add once a JDK+Android SDK / Mac are available.
- **`judge/tests/EventManager.Judge.Core.Tests`** — net10.0 xUnit/FsCheck.

## Core components
- **`SpokeEventLog`** — durable-before-ack write path (NFR-1.1): mint Snowflake id (U1 `IIdGenerator`, judge worker id) + judge `DeviceId` + contiguous sequence → `LocalEventQueue.EnqueueDurableAsync` (U2). The shared backbone for every judge write.
- **`ScoreCaptureService`** (US-402/403) — capture point-sparring / forms scores; **persist durably before returning** (the UI acks only after). The hub (U4b `ScoringIntakeService`) computes the authoritative outcome + advances; the judge only captures.
- **`MatQueueViewModel`** (US-401) — the assigned-mat match queue state; advances as hub pushes arrive.
- **`CrossMatViewModel`** (US-410) — read-only view of other mats; **never writes** (no enqueue path).
- **`FocusModeState`** (US-411) — focus/lock toggle for the active match.
- Pairing + sync reuse U2 `PairingClient` / `ISyncTransport` / `HubPushConsumer` (transport concrete = MAUI host seam).

## Tests
Durable-before-ack (score is queued before ack); point-sparring + forms capture; cross-mat view never enqueues; focus-mode toggle; queue advances on push.

## Deferred seams
MAUI UI shell interactions (visual); concrete SignalR/WSS transport + mDNS discovery (host wiring); Android/iOS/Mac heads (toolchain).
