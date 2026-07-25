# U5 Judge — Testing Guide (app-core verified; UI walkthrough pending)

**Unit**: U5 Judge (`judge/`) · **Date**: 2026-07-25
U5 is a UI unit, but the MAUI head is a **compiling Windows shell** (template pages) — the interactive
UI is not wired yet, so the manual click-through is **pending**. The app-core logic is fully tested.
See also [system testing guide](../../testing-guide.md) §5.

## App-core verification (the tested substance)
```bash
dotnet test judge/tests/EventManager.Judge.Core.Tests/EventManager.Judge.Core.Tests.csproj   # 6 passed
```
Covers:
- **Durable-before-ack** point-sparring capture (NFR-1.1): the score is in the local store / pending queue before the call returns.
- Contiguous per-device sequences (gap-free stream).
- Queued scores **drain after hub ack**.
- Mat queue advances on completion (US-401).
- Cross-mat view is **read-only** — a reflection assertion proves it has no write/capture method (US-410).
- Focus mode lock/unlock (US-411).

## Compile the MAUI Windows head
```bash
dotnet build judge/EventManager.Judge/EventManager.Judge.csproj    # net10.0-windows: Build succeeded
```
(Android head needs a JDK + Android SDK; iOS/Mac need macOS + Xcode — one-line TFM add once available.)

## Manual UI walkthrough — PENDING
The judge screens (pairing, mat queue, point-sparring/forms scoring pads, cross-mat view, focus/lock)
are not yet built on top of the core. When the UI is wired, the intended walkthrough is:
1. Pair via QR (U4a) → mat-scoped credential.
2. Score the current match → confirmation appears **only after** the event is durably queued.
3. Watch the queue advance; observe cross-mat view is read-only; toggle focus/lock.
Until then, the flows above are exercised through the app-core tests.

## Deferred seams
MAUI UI interactions; concrete SignalR/WSS transport + mDNS (host wiring); on-device SQLite/SQLCipher store (default is `InMemoryEventStore`); Android/iOS/Mac heads.
