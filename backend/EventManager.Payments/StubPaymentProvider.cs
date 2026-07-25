using System.Collections.Concurrent;

namespace EventManager.Payments;

/// <summary>
/// Mocked payment provider for MVP (D-06). Makes NO external call. Idempotent by
/// <see cref="PaymentRequest.IdempotencyKey"/> (BR-PAY-1). Default outcome is Succeeded; an
/// injectable selector lets tests/consumers force Declined/Timeout/Error paths (US-208).
/// </summary>
public sealed class StubPaymentProvider : IPaymentProvider
{
    private readonly Func<PaymentRequest, PaymentOutcome> _outcomeSelector;
    private readonly ConcurrentDictionary<string, PaymentResult> _byKey = new();

    public StubPaymentProvider(Func<PaymentRequest, PaymentOutcome>? outcomeSelector = null)
        => _outcomeSelector = outcomeSelector ?? (_ => PaymentOutcome.Succeeded);

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        // Idempotent: a repeated key returns the stored result (no double-charge).
        var result = _byKey.GetOrAdd(request.IdempotencyKey, _ => Build(request));
        return Task.FromResult(result);
    }

    private PaymentResult Build(PaymentRequest request)
    {
        var outcome = _outcomeSelector(request);
        var reference = $"STUB-{Guid.NewGuid():N}";
        string? reason = outcome switch
        {
            PaymentOutcome.Succeeded => null,
            PaymentOutcome.Declined => "Card declined (stub)",
            PaymentOutcome.Timeout => "Provider timeout (stub)",
            PaymentOutcome.Error => "Provider error (stub)",
            _ => "Unknown"
        };
        return new PaymentResult(outcome, reference, reason);
    }
}
