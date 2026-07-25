using EventManager.Payments;
using FsCheck.Xunit;
using Xunit;

namespace EventManager.Payments.Tests;

public class StubPaymentProviderTests
{
    private static PaymentRequest Req(string key, decimal amount = 50m) =>
        new(RegistrationId: 1, Amount: amount, Currency: "USD", IdempotencyKey: key);

    [Fact] // BR-PAY-2 default succeeds with a provider reference
    public async Task Default_Succeeds()
    {
        var result = await new StubPaymentProvider().ChargeAsync(Req("k1"));
        Assert.True(result.IsSuccess);
        Assert.StartsWith("STUB-", result.ProviderReference);
        Assert.Null(result.FailureReason);
    }

    [Fact] // BR-PAY-3 non-success carries a reason
    public async Task Declined_CarriesReason()
    {
        var provider = new StubPaymentProvider(_ => PaymentOutcome.Declined);
        var result = await provider.ChargeAsync(Req("k2"));
        Assert.Equal(PaymentOutcome.Declined, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.FailureReason));
    }

    [Property] // BR-PAY-1 idempotent: same key => same result reference
    public void Idempotent_SameKeySameResult(int rawAmount)
    {
        var provider = new StubPaymentProvider();
        var req = Req("stable-key", Math.Abs(rawAmount % 500) + 1);

        var r1 = provider.ChargeAsync(req).GetAwaiter().GetResult();
        var r2 = provider.ChargeAsync(req).GetAwaiter().GetResult();

        Assert.Equal(r1.ProviderReference, r2.ProviderReference);
        Assert.Equal(r1.Outcome, r2.Outcome);
    }

    [Fact] // different keys => distinct charges
    public async Task DifferentKeys_DistinctReferences()
    {
        var provider = new StubPaymentProvider();
        var a = await provider.ChargeAsync(Req("a"));
        var b = await provider.ChargeAsync(Req("b"));
        Assert.NotEqual(a.ProviderReference, b.ProviderReference);
    }

    [Theory]
    [InlineData(PaymentOutcome.Timeout)]
    [InlineData(PaymentOutcome.Error)]
    public async Task ForcedFailureOutcomes_AreHeldNotPaid(PaymentOutcome forced)
    {
        var provider = new StubPaymentProvider(_ => forced);
        var result = await provider.ChargeAsync(Req("k3"));
        Assert.Equal(forced, result.Outcome);
        Assert.False(result.IsSuccess); // US-208: unpaid-but-held path
    }
}
