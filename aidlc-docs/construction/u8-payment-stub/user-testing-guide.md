# U8 Payment Stub — Testing & Verification Guide

**Unit**: U8 (`EventManager.Payments`) — library, end-of-unit deliverable guide.
**Branch**: `unit/u8-payment-stub`

## Build & test
```bash
dotnet build backend/EventManager.Backend.slnx
dotnet test  backend/EventManager.Backend.slnx
```
Expected: build 0/0; **6 tests pass**.

## What the tests prove
| Test | Invariant |
|---|---|
| `Default_Succeeds` | default charge succeeds with a `STUB-` reference (BR-PAY-2) |
| `Declined_CarriesReason` | non-success carries a failure reason (BR-PAY-3) |
| `Idempotent_SameKeySameResult` | same IdempotencyKey ⇒ same result, charged once (BR-PAY-1) |
| `DifferentKeys_DistinctReferences` | distinct keys ⇒ distinct charges |
| `ForcedFailureOutcomes_AreHeldNotPaid` | timeout/error ⇒ not paid (US-208 held+retry path) |

## How a consumer (U3) uses it
```csharp
IPaymentProvider payments = new StubPaymentProvider();            // MVP
var result = await payments.ChargeAsync(new PaymentRequest(regId, amount, "USD", idempotencyKey));
registration.PaymentStatus = result.IsSuccess ? PaymentStatus.Paid : PaymentStatus.Owed; // held + retry
```
To simulate failures in tests, pass an outcome selector: `new StubPaymentProvider(_ => PaymentOutcome.Declined)`.
No live payments occur in MVP (D-06).
