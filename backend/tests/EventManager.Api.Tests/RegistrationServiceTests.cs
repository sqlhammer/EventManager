using EventManager.Api.Services;
using EventManager.Domain;
using EventManager.Payments;

namespace EventManager.Api.Tests;

/// <summary>PBT-2 + examples: no double-registration (BR-REG-5), atomic bulk (Q2=A), payment paths.</summary>
public sealed class RegistrationServiceTests
{
    [Fact]
    public async Task Register_then_register_same_division_is_rejected() // PBT-2 (example form)
    {
        using var h = new TestHost();
        var (_, divisionId, athleteId, accountId) = await h.SeedOpenEventAsync();

        var first = await h.Registrations.RegisterAsync(accountId,
            new RegisterInput(EventOf(h), athleteId, [divisionId], PayByCard: false));
        Assert.False(first.IsError);

        var second = await h.Registrations.RegisterAsync(accountId,
            new RegisterInput(EventOf(h), athleteId, [divisionId], PayByCard: false));
        Assert.True(second.IsError);   // no athlete double-registered for the same division
    }

    [Fact]
    public async Task Bulk_with_one_conflict_commits_nothing() // BR-REG-6 atomicity
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, accountId) = await h.SeedOpenEventAsync();
        // second athlete ineligible (wrong gender)
        var badAthlete = await h.Registrations.UpsertProfileAsync(accountId, null,
            new ProfileInput("Bad", new DateOnly(2000, 1, 1), 5, 60, "Academy A", "F"));

        var batch = new BatchRegisterInput(eventId,
            [new BatchEntry(athleteId, [divisionId]), new BatchEntry(badAthlete.Value, [divisionId])],
            PayByCard: false, IdempotencyKey: "batch-1");

        var result = await h.Registrations.RegisterBatchAsync(accountId, batch);
        Assert.True(result.IsError);                                  // whole batch rejected
        Assert.Empty(h.Db.RegistrationRows.ToList());                // nothing committed
    }

    [Fact]
    public async Task Bulk_resubmit_with_same_key_is_idempotent() // BR-REG-7
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, accountId) = await h.SeedOpenEventAsync();
        var batch = new BatchRegisterInput(eventId, [new BatchEntry(athleteId, [divisionId])], false, "batch-key-x");

        var first = await h.Registrations.RegisterBatchAsync(accountId, batch);
        var second = await h.Registrations.RegisterBatchAsync(accountId, batch);

        Assert.False(first.IsError);
        Assert.False(second.IsError);
        Assert.Equal(first.Value.RegistrationIds.Count, second.Value.RegistrationIds.Count);
        Assert.Single(h.Db.RegistrationRows.ToList());               // not double-registered
    }

    [Fact]
    public async Task Card_decline_leaves_registration_owed() // BR-PAY-3
    {
        using var h = new TestHost(_ => PaymentOutcome.Declined);
        var (eventId, divisionId, athleteId, accountId) = await h.SeedOpenEventAsync();
        await h.Events.SetPaymentOptionsAsync(accountId, eventId, cardEnabled: true);

        var result = await h.Registrations.RegisterAsync(accountId,
            new RegisterInput(eventId, athleteId, [divisionId], PayByCard: true));

        Assert.False(result.IsError);
        Assert.Equal(nameof(PaymentStatus.Owed), result.Value.PaymentStatus);
    }

    [Fact]
    public async Task Registration_when_window_closed_is_rejected() // BR-REG-4
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, accountId) = await h.SeedOpenEventAsync();
        await h.Events.SetRegistrationOpenAsync(accountId, eventId, false);

        var result = await h.Registrations.RegisterAsync(accountId,
            new RegisterInput(eventId, athleteId, [divisionId], PayByCard: false));
        Assert.True(result.IsError);
    }

    private static long EventOf(TestHost h) => h.Db.EventRows.Select(e => e.EventId).First();
}
