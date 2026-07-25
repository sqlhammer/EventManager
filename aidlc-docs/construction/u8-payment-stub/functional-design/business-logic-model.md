# U8 Payment Stub — Functional Design (fast-tracked)

**Branch**: `unit/u8-payment-stub` · **Date**: 2026-07-25
**Unit**: U8 = `EventManager.Payments` — the stubbed/mocked payment-provider abstraction (D-06). Consumed by U3 registration (FR-2.4, US-208). **No live Stripe in MVP.**

## Domain types
| Type | Shape |
|---|---|
| `PaymentOutcome` | `Succeeded \| Declined \| Timeout \| Error` (enum) |
| `PaymentRequest` | `{ RegistrationId, Amount, Currency, IdempotencyKey }` |
| `PaymentResult` | `{ Outcome, ProviderReference, FailureReason? }` |
| `IPaymentProvider` | `Task<PaymentResult> ChargeAsync(PaymentRequest, ct)` |

## Behavior (`StubPaymentProvider`)
```
ChargeAsync(request):
   if seen(request.IdempotencyKey): return the stored result      # idempotent, no double-charge
   outcome = outcomeSelector(request)                             # default: Succeeded; test-configurable
   result = new PaymentResult(outcome, providerRef = "STUB-" + newGuid, failureReason for non-success)
   store(request.IdempotencyKey -> result)
   return result
```
- **No network / no live provider** — purely in-memory simulation (D-06).
- Decline/Timeout/Error paths are first-class so U3 can exercise "unpaid-but-held + retry" (US-208).
- Pay-at-door is **not** a payment call — it is a registration state (`PaymentStatus.Owed`) handled in U3; U8 only covers the card path abstraction.

## Business rules (PBT candidates)
| Rule | Statement |
|---|---|
| BR-PAY-1 | Idempotent: same `IdempotencyKey` ⇒ same `PaymentResult`, charged at most once |
| BR-PAY-2 | Default outcome is `Succeeded` with a non-empty `ProviderReference` |
| BR-PAY-3 | Non-success outcomes carry a `FailureReason`; success does not |
| BR-PAY-4 | No live external call is ever made (stub only) |

## Mapping to registration (consumer, U3 — noted, not built here)
`Succeeded → PaymentStatus.Paid`; `Declined/Timeout/Error → PaymentStatus.Owed` (held, retryable). U8 stays self-contained (no dependency on Domain); U3 does the mapping.

## Not applicable
Frontend/migrations/deployment — N/A (library). Infrastructure Design — SKIP.
