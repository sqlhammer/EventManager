# U6 Check-In App — Code & Verification Summary

**Stage**: CONSTRUCTION → Code Generation (fast-tracked) · **Unit**: U6 Check-In · **Date**: 2026-07-25
Branch `unit/u6-checkin` · Code under CS-1 (no ternaries). **Final unit in the build order.**

## What shipped
- **`checkin/EventManager.Checkin.Core`** (net10.0) — testable app logic (consumes U1 `WeighInPolicyEvaluator` + U2 `LocalEventQueue`). Shares the `SpokeEventLog`/`InMemoryEventStore` pattern with U5 (copied per app; apps are independent per D-07).
- **`checkin/EventManager.Checkin`** — **MAUI Windows head that COMPILES** (`net10.0-windows10.0.19041.0`); references the core; DI root in `MauiProgram`. **Build succeeded.**
- **`checkin/tests/EventManager.Checkin.Core.Tests`** — xUnit; **5 tests passing**.

## Core components
| Component | Responsibility | Stories |
|---|---|---|
| `CheckInService` | Mark athlete present — durable append-only event before ack | US-306 |
| `WeighInService` | Weigh-in with instant range validation via U1 `WeighInPolicyEvaluator`; immutable history; optional **non-binding** staff recommendation (D-25) | US-307 |
| `SpokeEventLog` / `InMemoryEventStore` | Durable-before-ack write path + default store (SQLite/SQLCipher is a host seam) | NFR-1.1 |

## Tests (5)
Check-in durable-before-ack; in-range weigh-in → green + recorded; out-of-range → flagged (routes to policy flow); non-binding recommendation attached (D-25); corrections are new events (immutable history).

## MAUI head status
Windows head compiles. Android head can't build (no JDK/Android SDK); iOS/Mac need a Mac. Other TFMs are a one-line add later.

## Verify
```bash
dotnet test  checkin/tests/EventManager.Checkin.Core.Tests/EventManager.Checkin.Core.Tests.csproj   # 5 passed
dotnet build checkin/EventManager.Checkin/EventManager.Checkin.csproj                                 # Windows head: Build succeeded
```
