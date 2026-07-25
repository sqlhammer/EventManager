# U8 Payment Stub — Code Summary

**Branch**: `unit/u8-payment-stub` · **Date**: 2026-07-25 (fast-tracked)

## Created — application code (stands up `backend/`)
```
backend/
  Directory.Build.props
  EventManager.Backend.slnx
  EventManager.Payments/
    Payments.cs             PaymentOutcome, PaymentRequest, PaymentResult, IPaymentProvider (seam, D-06)
    StubPaymentProvider.cs  in-memory mock: idempotent by key, injectable outcome, no external call
  tests/EventManager.Payments.Tests/
    StubPaymentProviderTests.cs  default success, decline reason, idempotency (FsCheck), forced timeout/error
```

## Verification
- `dotnet build backend/EventManager.Backend.slnx` → **succeeded (0/0)**.
- `dotnet test` → **6 passed / 0 failed**.

## Coverage
US-208 (card path stubbed) + BR-PAY-1..4: idempotency, default success, failure reasons, held-not-paid on decline/timeout/error. No live provider.

## Notes
- U8 self-contained (BCL only); U3 will map `PaymentOutcome → PaymentStatus` (Succeeded→Paid, else Owed/held).
- Real Stripe adapter drops into `IPaymentProvider` post-MVP with no consumer change.
- N/A: API/repository/frontend/migrations/deployment (this unit is just the provider seam + stub).
