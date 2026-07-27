using EventManager.Api.Auth;
using ErrorOr;

namespace EventManager.Api.Tests;

/// <summary>
/// US-709 — non-disclosure and resource-id probing resistance (BR-READ-18/19/20).
/// The load-bearing security tests for this unit: SECURITY-08 is a blocking rule.
/// </summary>
public sealed class ReadNonDisclosureTests
{
    [Fact] // BR-READ-18: "forbidden" and "does not exist" are the same answer
    public async Task No_tier_and_unknown_event_return_identical_errors()
    {
        using var h = new TestHost();
        var (closed, _, _, organizer) = await h.SeedOpenEventAsync();
        await h.Events.SetRegistrationOpenAsync(organizer, closed, false);
        long stranger = h.Ids.NextId();

        var forbidden = await h.EventQueries.GetAsync(closed, AccessTier.None);
        var missing = await h.EventQueries.GetAsync(999_999, AccessTier.None);

        Assert.True(forbidden.IsError);
        Assert.True(missing.IsError);
        Assert.Equal(missing.FirstError.Code, forbidden.FirstError.Code);
        Assert.Equal(missing.FirstError.Description, forbidden.FirstError.Description);
        Assert.Equal(missing.FirstError.Type, forbidden.FirstError.Type);
    }

    [Fact] // BR-READ-20: read endpoints never return 403 — a 403 would confirm existence
    public async Task Insufficient_tier_is_never_forbidden()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId);

        ErrorOr<object>[] denials =
        [
            await h.EventQueries.GetAsync(eventId, AccessTier.None),
        ];
        foreach (var denial in denials) Assert.Equal(ErrorType.NotFound, denial.FirstError.Type);

        Assert.Equal(ErrorType.NotFound, (await h.RegistrantQueries.ListAsync(eventId, AccessTier.Public, false)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await h.RegistrantQueries.ListAsync(eventId, AccessTier.Registrant, false)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await h.AccountQueries.ListAsync(eventId, AccessTier.Public)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await h.AccountQueries.ListAsync(eventId, AccessTier.Registrant)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await h.DivisionQueries.ListAsync(eventId, AccessTier.None, false)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound, (await h.PolicyQueries.GetAsync(eventId, AccessTier.None)).FirstError.Type);
        Assert.Equal(ErrorType.NotFound,
            (await h.RegistrantQueries.GetAsync(eventId, registrationId, AccessTier.None, parent)).FirstError.Type);
    }

    [Fact] // BR-READ-19: a division from another event is invisible under this event's path
    public async Task Division_from_another_event_is_not_readable()
    {
        using var h = new TestHost();
        var (eventA, _, _, _) = await h.SeedOpenEventAsync();
        var (eventB, divisionB, _, _) = await h.SeedOpenEventAsync();

        var probe = await h.DivisionQueries.GetAsync(eventA, divisionB, AccessTier.Organizer);

        Assert.True(probe.IsError);
        Assert.Equal(ErrorType.NotFound, probe.FirstError.Type);
    }

    [Fact] // BR-READ-19: likewise for registrations
    public async Task Registration_from_another_event_is_not_readable()
    {
        using var h = new TestHost();
        var (eventA, _, _, organizerA) = await h.SeedOpenEventAsync();
        var (eventB, divisionB, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationB = await h.RegisterAsync(parent, eventB, divisionB);

        var probe = await h.RegistrantQueries.GetAsync(eventA, registrationB, AccessTier.Organizer, organizerA);

        Assert.True(probe.IsError);
    }

    [Fact] // BR-READ-10: an account with no role on this event is never confirmed to exist
    public async Task Account_without_a_role_on_this_event_is_not_readable()
    {
        using var h = new TestHost();
        var (eventA, _, _, _) = await h.SeedOpenEventAsync();
        var (_, _, _, organizerB) = await h.SeedOpenEventAsync();
        await h.SeedIdentityAsync(organizerB, "other-organizer@example.com");

        var probe = await h.AccountQueries.GetAsync(eventA, organizerB, AccessTier.Organizer);

        Assert.True(probe.IsError);
        Assert.Equal(ErrorType.NotFound, probe.FirstError.Type);
    }

    [Fact] // BR-READ-18: probing a nonexistent account id looks the same as probing a real one
    public async Task Unrelated_and_nonexistent_account_probes_are_indistinguishable()
    {
        using var h = new TestHost();
        var (eventA, _, _, _) = await h.SeedOpenEventAsync();
        var (_, _, _, organizerB) = await h.SeedOpenEventAsync();

        var realButUnrelated = await h.AccountQueries.GetAsync(eventA, organizerB, AccessTier.Organizer);
        var neverExisted = await h.AccountQueries.GetAsync(eventA, 987_654_321, AccessTier.Organizer);

        Assert.Equal(neverExisted.FirstError.Code, realButUnrelated.FirstError.Code);
        Assert.Equal(neverExisted.FirstError.Description, realButUnrelated.FirstError.Description);
    }
}
