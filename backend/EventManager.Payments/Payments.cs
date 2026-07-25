namespace EventManager.Payments;

public enum PaymentOutcome { Succeeded, Declined, Timeout, Error }

/// <summary>A request to charge the card path. IdempotencyKey makes retries safe (BR-PAY-1).</summary>
public sealed record PaymentRequest(long RegistrationId, decimal Amount, string Currency, string IdempotencyKey);

public sealed record PaymentResult(PaymentOutcome Outcome, string ProviderReference, string? FailureReason = null)
{
    public bool IsSuccess => Outcome == PaymentOutcome.Succeeded;
}

/// <summary>
/// Payment provider seam (D-06). MVP uses <see cref="StubPaymentProvider"/>; a real adapter
/// (e.g., Stripe) drops in later without touching consumers.
/// </summary>
public interface IPaymentProvider
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}
