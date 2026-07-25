# U8 Payment Stub — NFR Requirements + Design (fast-tracked, consolidated)

**Branch**: `unit/u8-payment-stub` · **Date**: 2026-07-25

Small unit; NFR/tech decisions consolidated.

## NFR requirements
| ID | Requirement |
|---|---|
| U8-SEC-1 | Provider is behind an `IPaymentProvider` seam so no live-payment code exists in MVP (D-06); no secrets, no card data stored |
| U8-REL-1 | Idempotent charge by `IdempotencyKey` — safe to retry after a client/network hiccup (BR-PAY-1) |
| U8-TEST-1 | FsCheck idempotency property + example tests for each outcome; ≥90% coverage (tiny unit) |
| U8-MAINT-1 | Self-contained (BCL only); swappable for a real provider later without touching consumers |

## Tech-stack decisions
| # | Decision | Rationale |
|---|---|---|
| U8-TSD-1 | Pure `IPaymentProvider` abstraction + `StubPaymentProvider`; outcome via injectable selector | test-configurable decline/timeout; zero external deps |
| U8-TSD-2 | Location: `backend/EventManager.Payments` (new `backend/` solution) | cloud-only concern (D-07); U8 stands up the backend solution ahead of U3 |
| U8-TSD-3 | No third-party runtime dependency | stub only |

## NFR design patterns
- **Strategy/Adapter seam** (`IPaymentProvider`) — real Stripe adapter drops in post-MVP.
- **Idempotency key store** — dedupe map keyed by `IdempotencyKey`.
- Infra components (retry/circuit-breaker around a *real* provider): **N/A** in MVP; added with the real adapter.

## Infrastructure Design
**SKIPPED** — library, no infra.
