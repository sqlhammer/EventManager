using EventManager.Api.Auth;
using EventManager.Domain;

namespace EventManager.Api.Tests;

/// <summary>
/// US-701/702/703 — the authoritative tier stories. Tier qualification, cumulative grants, and the
/// rules that govern every read endpoint (BR-READ-1..6).
/// </summary>
public sealed class ReadTierTests
{
    [Fact] // US-701: an open event is visible to any authenticated caller
    public async Task Open_event_grants_public_tier_to_a_stranger()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();
        long stranger = h.Ids.NextId();

        Assert.Equal(AccessTier.Public, await h.ReadAuth.ResolveAsync(stranger, eventId));
    }

    [Fact] // US-701: a draft event is not discoverable
    public async Task Draft_event_grants_no_tier_to_a_stranger()
    {
        using var h = new TestHost();
        long organizer = h.Ids.NextId();
        var created = await h.Events.CreateEventAsync(organizer, new Services.CreateEventInput(
            "Draft", "Dojo", new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20),
            50m, nameof(WeighInPolicyMode.Strict), null));
        long stranger = h.Ids.NextId();

        Assert.Equal(AccessTier.None, await h.ReadAuth.ResolveAsync(stranger, created.Value));
    }

    [Fact] // US-701: closed registration removes public discoverability
    public async Task Closed_event_grants_no_tier_to_a_stranger()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();
        await h.Events.SetRegistrationOpenAsync(organizer, eventId, false);
        long stranger = h.Ids.NextId();

        Assert.Equal(AccessTier.None, await h.ReadAuth.ResolveAsync(stranger, eventId));
    }

    [Fact] // US-702: a registration lifts the caller above Public
    public async Task Registration_grants_registrant_tier()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        await h.RegisterAsync(parent, eventId, divisionId);

        Assert.Equal(AccessTier.Registrant, await h.ReadAuth.ResolveAsync(parent, eventId));
    }

    [Fact] // US-702: withdrawing drops the caller back to Public while the event is still open
    public async Task Withdrawn_registration_falls_back_to_public()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId);

        await h.Registrations.WithdrawAsync(parent, registrationId);

        Assert.Equal(AccessTier.Public, await h.ReadAuth.ResolveAsync(parent, eventId));
    }

    [Fact] // US-702 + US-701: withdrawn on a closed event leaves no access at all
    public async Task Withdrawn_registration_on_closed_event_grants_nothing()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, organizer) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId);
        await h.Registrations.WithdrawAsync(parent, registrationId);
        await h.Events.SetRegistrationOpenAsync(organizer, eventId, false);

        Assert.Equal(AccessTier.None, await h.ReadAuth.ResolveAsync(parent, eventId));
    }

    [Fact] // US-703: the creating organizer holds the top tier
    public async Task Organizer_holds_organizer_tier()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();

        Assert.Equal(AccessTier.Organizer, await h.ReadAuth.ResolveAsync(organizer, eventId));
    }

    [Fact] // US-703: organizer tier survives closing registration — unlike Public
    public async Task Organizer_tier_is_independent_of_registration_status()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();
        await h.Events.SetRegistrationOpenAsync(organizer, eventId, false);

        Assert.Equal(AccessTier.Organizer, await h.ReadAuth.ResolveAsync(organizer, eventId));
    }

    [Fact] // US-703: Full Admin and Co-Organizer read identically (BR-READ-3)
    public async Task Co_organizer_resolves_to_the_same_tier_as_full_admin()
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        long coOrganizer = h.Ids.NextId();
        await h.OrganizerRoles.AddExistingAsync(admin, eventId, coOrganizer);

        Assert.Equal(AccessTier.Organizer, await h.ReadAuth.ResolveAsync(admin, eventId));
        Assert.Equal(AccessTier.Organizer, await h.ReadAuth.ResolveAsync(coOrganizer, eventId));

        var adminView = await h.EventQueries.GetAsync(eventId, AccessTier.Organizer);
        var coView = await h.EventQueries.GetAsync(eventId, AccessTier.Organizer);
        Assert.Equal(adminView.Value, coView.Value);   // identical data, no role-based redaction
    }

    [Fact] // BR-READ-6: tier is per event, never global
    public async Task Tier_is_resolved_per_event()
    {
        using var h = new TestHost();
        var (mine, _, _, me) = await h.SeedOpenEventAsync();
        var (theirs, _, _, _) = await h.SeedOpenEventAsync();

        Assert.Equal(AccessTier.Organizer, await h.ReadAuth.ResolveAsync(me, mine));
        Assert.Equal(AccessTier.Public, await h.ReadAuth.ResolveAsync(me, theirs));
    }

    [Fact] // BR-READ-1: an event that does not exist yields no tier
    public async Task Unknown_event_grants_no_tier()
    {
        using var h = new TestHost();
        Assert.Equal(AccessTier.None, await h.ReadAuth.ResolveAsync(h.Ids.NextId(), 999_999));
    }

    [Fact] // US-704: the collection spans all three tiers and tags each entry
    public async Task Collection_spans_tiers_and_tags_each_event()
    {
        using var h = new TestHost();
        var (mine, _, _, me) = await h.SeedOpenEventAsync();
        var (entered, enteredDivision, _, _) = await h.SeedOpenEventAsync();
        var (open, _, _, _) = await h.SeedOpenEventAsync();
        await h.RegisterAsync(me, entered, enteredDivision);

        var items = await h.EventQueries.ListAsync(me);
        var byId = items.ToDictionary(i => i.EventId);

        Assert.Equal(nameof(AccessTier.Organizer), byId[mine].AccessTier);
        Assert.Equal(nameof(OrganizerRole.FullAdmin), byId[mine].OrganizerRole);
        Assert.Equal(nameof(AccessTier.Registrant), byId[entered].AccessTier);
        Assert.Null(byId[entered].OrganizerRole);
        Assert.Equal(nameof(AccessTier.Public), byId[open].AccessTier);
    }

    [Fact] // US-704: a closed event a caller has no relationship with never appears
    public async Task Collection_omits_events_with_no_tier()
    {
        using var h = new TestHost();
        var (closed, _, _, organizer) = await h.SeedOpenEventAsync();
        await h.Events.SetRegistrationOpenAsync(organizer, closed, false);
        long stranger = h.Ids.NextId();

        var items = await h.EventQueries.ListAsync(stranger);
        Assert.DoesNotContain(items, i => i.EventId == closed);
    }
}
