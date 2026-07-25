# U6 Check-In — Testing Guide (app-core verified; UI walkthrough pending)

**Unit**: U6 Check-In (`checkin/`) · **Date**: 2026-07-25
UI unit; the MAUI head is a **compiling Windows shell** (template pages) — the interactive UI is not
wired yet, so the manual click-through is **pending**. The app-core logic is fully tested.
See also [system testing guide](../../testing-guide.md) §5.

## App-core verification (the tested substance)
```bash
dotnet test checkin/tests/EventManager.Checkin.Core.Tests/EventManager.Checkin.Core.Tests.csproj   # 5 passed
```
Covers:
- **Check-in durable-before-ack** (US-306): marking present is persisted before the call returns.
- **In-range weigh-in → green + recorded** (US-307): via the U1 `WeighInPolicyEvaluator`.
- **Out-of-range weigh-in → flagged** (routes to the organizer's policy flow).
- **Non-binding staff recommendation** attached (D-25) — recorded on the event, advisory only.
- **Corrections are new events** (immutable history) — a re-weigh appends, never overwrites.

## Compile the MAUI Windows head
```bash
dotnet build checkin/EventManager.Checkin/EventManager.Checkin.csproj   # net10.0-windows: Build succeeded
```
(Android head needs a JDK + Android SDK; iOS/Mac need macOS + Xcode.)

## Manual UI walkthrough — PENDING
The check-in screens (pairing, athlete search + present, weigh-in pad with green/flag feedback, optional
recommendation) are not yet built on top of the core. Intended walkthrough once wired:
1. Pair via QR (U4a).
2. Search an athlete → mark present (durable event).
3. Record a weight → instant green (in range) or flag (out of range) with an optional recommendation.
Until then, the flows above are exercised through the app-core tests.

## Deferred seams
MAUI UI interactions; concrete transport + mDNS (host wiring); on-device SQLite/SQLCipher store; Android/iOS/Mac heads. The per-division status board (US-310) is owned by the hub (U4b).
