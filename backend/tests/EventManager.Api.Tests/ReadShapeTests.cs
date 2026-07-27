using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Domain;

namespace EventManager.Api.Tests;

/// <summary>
/// US-704..US-708 — response shape, inclusion flags, and the deleted-account rule (BR-READ-7..17).
/// </summary>
public sealed class ReadShapeTests
{
    [Fact] // BR-READ-7: Public gets the summary shape and nothing more
    public async Task Public_tier_receives_the_summary_shape()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        var result = await h.EventQueries.GetAsync(eventId, AccessTier.Public);

        var summary = Assert.IsType<EventSummaryResponse>(result.Value);
        Assert.Equal("Test Open", summary.Name);
    }

    [Fact] // Q4=C: the registration window is surfaced at the public tier
    public async Task Public_summary_surfaces_the_registration_window()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        var result = await h.EventQueries.GetAsync(eventId, AccessTier.Public);

        var summary = Assert.IsType<EventSummaryResponse>(result.Value);
        Assert.Equal(new DateOnly(2026, 8, 1), summary.RegistrationStart);
        Assert.Equal(new DateOnly(2026, 8, 20), summary.RegistrationEnd);
    }

    [Fact] // BR-READ-7: Registrant and Organizer get the detail shape
    public async Task Registrant_and_organizer_receive_the_detail_shape()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        Assert.IsType<EventDetailResponse>((await h.EventQueries.GetAsync(eventId, AccessTier.Registrant)).Value);
        Assert.IsType<EventDetailResponse>((await h.EventQueries.GetAsync(eventId, AccessTier.Organizer)).Value);
    }

    [Fact] // BR-READ-12: tolerance is absent unless the mode is Tolerance
    public async Task Tolerance_is_omitted_for_strict_policy()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        var policy = await h.PolicyQueries.GetAsync(eventId, AccessTier.Public);

        Assert.Equal(nameof(WeighInPolicyMode.Strict), policy.Value.Mode);
        Assert.Null(policy.Value.TolerancePercent);
    }

    [Fact] // BR-READ-12: tolerance is present under the Tolerance mode
    public async Task Tolerance_is_present_for_tolerance_policy()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();
        await h.Events.SetWeighInPolicyAsync(organizer, eventId, nameof(WeighInPolicyMode.Tolerance), 2.5);

        var policy = await h.PolicyQueries.GetAsync(eventId, AccessTier.Public);

        Assert.Equal(nameof(WeighInPolicyMode.Tolerance), policy.Value.Mode);
        Assert.Equal(2.5, policy.Value.TolerancePercent);
    }

    [Fact] // BR-READ-15: completed divisions are hidden unless requested
    public async Task Completed_divisions_are_excluded_by_default()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        var row = await h.Db.DivisionRows.FindAsync(divisionId);
        row!.Status = nameof(DivisionStatus.Complete);
        await h.Db.SaveChangesAsync();

        var withoutFlag = await h.DivisionQueries.ListAsync(eventId, AccessTier.Public, includeCompleted: false);
        var withFlag = await h.DivisionQueries.ListAsync(eventId, AccessTier.Public, includeCompleted: true);

        Assert.Empty(withoutFlag.Value);
        Assert.Single(withFlag.Value);
    }

    [Fact] // BR-READ-8: the roster is Organizer-only and carries no profile data
    public async Task Registrant_list_is_organizer_only_and_omits_profile_fields()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, organizer) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        await h.RegisterAsync(parent, eventId, divisionId, name: "Kid One");

        Assert.True((await h.RegistrantQueries.ListAsync(eventId, AccessTier.Public, false)).IsError);
        Assert.True((await h.RegistrantQueries.ListAsync(eventId, AccessTier.Registrant, false)).IsError);

        var roster = await h.RegistrantQueries.ListAsync(eventId, AccessTier.Organizer, false);
        var entry = Assert.Single(roster.Value);
        Assert.Equal("Kid One", entry.AthleteName);
        // The list item type simply has no profile members — the shape enforces BR-READ-8.
        Assert.IsType<RegistrantListItemResponse>(entry);
    }

    [Fact] // BR-READ-9: detail adds the profile fields organizers need for weigh-in
    public async Task Registrant_detail_adds_profile_fields()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, organizer) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId, weight: 72.5, age: 14);

        var detail = await h.RegistrantQueries.GetAsync(eventId, registrationId, AccessTier.Organizer, organizer);

        Assert.Equal(72.5, detail.Value.Weight);
        Assert.NotNull(detail.Value.DateOfBirth);
        Assert.NotNull(detail.Value.Rank);
    }

    [Fact] // BR-READ-9: a registrant reads their own record, not someone else's
    public async Task Registrant_reads_own_record_only()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long mine = h.Ids.NextId();
        long theirs = h.Ids.NextId();
        var myRegistration = await h.RegisterAsync(mine, eventId, divisionId, name: "Mine");
        var theirRegistration = await h.RegisterAsync(theirs, eventId, divisionId, name: "Theirs");

        var own = await h.RegistrantQueries.GetAsync(eventId, myRegistration, AccessTier.Registrant, mine);
        var other = await h.RegistrantQueries.GetAsync(eventId, theirRegistration, AccessTier.Registrant, mine);

        Assert.False(own.IsError);
        Assert.True(other.IsError);
    }

    [Fact] // BR-READ-14: withdrawn registrations are hidden unless requested
    public async Task Withdrawn_registrations_are_excluded_by_default()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId);
        await h.Registrations.WithdrawAsync(parent, registrationId);

        var withoutFlag = await h.RegistrantQueries.ListAsync(eventId, AccessTier.Organizer, includeWithdrawn: false);
        var withFlag = await h.RegistrantQueries.ListAsync(eventId, AccessTier.Organizer, includeWithdrawn: true);

        Assert.Empty(withoutFlag.Value);
        Assert.True(Assert.Single(withFlag.Value).Withdrawn);
    }

    [Fact] // BR-READ-16 (Q2=A): deleting the managing account does not remove the athlete's entry
    public async Task Registration_survives_deletion_of_its_managing_account()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        await h.SeedIdentityAsync(parent, "parent@example.com");
        await h.RegisterAsync(parent, eventId, divisionId, name: "Kid Of Deleted Parent");

        // Simulate the identity-plane effect of US-110: the account is anonymized and soft-deleted.
        var user = h.Db.Users.Single(u => u.AccountId == parent);
        user.DeletedAt = DateTimeOffset.UtcNow;
        user.Email = $"deleted-{parent}@deleted.invalid";
        await h.Db.SaveChangesAsync();

        var roster = await h.RegistrantQueries.ListAsync(eventId, AccessTier.Organizer, false);

        // The athlete is still competing, so the organizer must still see them on the mat.
        Assert.Equal("Kid Of Deleted Parent", Assert.Single(roster.Value).AthleteName);
    }

    [Fact] // BR-READ-10/11: the account roster carries role and email, and no credential material
    public async Task Account_roster_returns_role_and_email()
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        await h.SeedIdentityAsync(admin, "admin@example.com");

        var roster = await h.AccountQueries.ListAsync(eventId, AccessTier.Organizer);

        var entry = Assert.Single(roster.Value);
        Assert.Equal(admin, entry.AccountId);
        Assert.Equal("admin@example.com", entry.Email);
        Assert.Equal(nameof(OrganizerRole.FullAdmin), entry.Role);
        Assert.IsType<OrganizerAccountResponse>(entry);   // shape carries nothing else
    }

    [Fact] // BR-READ-13: collections are complete and unpaginated
    public async Task Roster_returns_every_registration()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = await h.SeedOpenEventAsync();
        for (var i = 0; i < 25; i++) await h.RegisterAsync(h.Ids.NextId(), eventId, divisionId, name: $"Athlete {i}");

        var roster = await h.RegistrantQueries.ListAsync(eventId, AccessTier.Organizer, false);

        Assert.Equal(25, roster.Value.Count);
    }
}
