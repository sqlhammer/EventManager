using EventManager.Api.Auth;
using EventManager.Api.Services;

namespace EventManager.Api.Tests;

/// <summary>
/// US-710 — conditional requests (BR-READ-22..26), including the U9-CON-2 staleness criterion.
/// </summary>
public sealed class ReadEtagTests
{
    [Fact] // The watermark tracks the event's log position
    public async Task Watermark_advances_when_the_event_log_grows()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();

        var before = await h.Etags.WatermarkAsync(eventId);
        await h.Events.SetPaymentOptionsAsync(organizer, eventId, cardEnabled: true);
        var after = await h.Etags.WatermarkAsync(eventId);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after > before);
    }

    [Fact] // BR-READ-22: a write changes the ETag
    public async Task Etag_changes_after_a_write_to_the_event()
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = await h.SeedOpenEventAsync();

        var before = h.Etags.Build("event", eventId, await h.Etags.WatermarkAsync(eventId), AccessTier.Organizer);
        await h.Events.SetPaymentOptionsAsync(organizer, eventId, cardEnabled: true);
        var after = h.Etags.Build("event", eventId, await h.Etags.WatermarkAsync(eventId), AccessTier.Organizer);

        Assert.NotEqual(before, after);
    }

    [Fact] // BR-READ-22: an unchanged event yields a stable ETag
    public async Task Etag_is_stable_while_nothing_changes()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        var first = h.Etags.Build("event", eventId, await h.Etags.WatermarkAsync(eventId), AccessTier.Public);
        var second = h.Etags.Build("event", eventId, await h.Etags.WatermarkAsync(eventId), AccessTier.Public);

        Assert.Equal(first, second);
    }

    [Fact]
    // BR-READ-22, the important one: the ETag must cover the caller's TIER, not just the watermark.
    // Otherwise a caller who gained a tier (say, by registering) would present their old
    // If-None-Match and get a 304 saying "nothing changed" while still holding the narrower
    // Public body — silently withholding data they are now entitled to.
    public async Task Etag_differs_per_tier_at_the_same_watermark()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();
        var watermark = await h.Etags.WatermarkAsync(eventId);

        var asPublic = h.Etags.Build("event", eventId, watermark, AccessTier.Public);
        var asRegistrant = h.Etags.Build("event", eventId, watermark, AccessTier.Registrant);
        var asOrganizer = h.Etags.Build("event", eventId, watermark, AccessTier.Organizer);

        Assert.Equal(3, new HashSet<string> { asPublic, asRegistrant, asOrganizer }.Count);
    }

    [Fact] // BR-READ-22: inclusion flags change the body, so they must change the ETag
    public async Task Etag_differs_per_inclusion_flag()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();
        var watermark = await h.Etags.WatermarkAsync(eventId);

        var excluding = h.Etags.Build("registrants", eventId, watermark, AccessTier.Organizer, "False");
        var including = h.Etags.Build("registrants", eventId, watermark, AccessTier.Organizer, "True");

        Assert.NotEqual(excluding, including);
    }

    [Fact] // BR-READ-22: different endpoints at the same watermark are distinct
    public async Task Etag_differs_per_endpoint()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();
        var watermark = await h.Etags.WatermarkAsync(eventId);

        Assert.NotEqual(
            h.Etags.Build("event", eventId, watermark, AccessTier.Organizer),
            h.Etags.Build("divisions", eventId, watermark, AccessTier.Organizer));
    }

    [Fact] // BR-READ-23: the raw watermark must not be recoverable from the token
    public async Task Etag_does_not_expose_the_raw_watermark()
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();
        var watermark = await h.Etags.WatermarkAsync(eventId);

        var etag = h.Etags.Build("event", eventId, watermark, AccessTier.Public);

        Assert.DoesNotContain(watermark!.Value.ToString(), etag);
        Assert.DoesNotContain(eventId.ToString(), etag);
    }

    [Theory] // BR-READ-24: If-None-Match handling, including weak tags and the wildcard
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("\"deadbeef\"", false)]
    public void Non_matching_if_none_match_does_not_short_circuit(string? header, bool expected)
    {
        Assert.Equal(expected, ReadEtagProvider.Matches(header, "\"abc123\""));
    }

    [Fact]
    public void Matching_if_none_match_short_circuits()
    {
        Assert.True(ReadEtagProvider.Matches("\"abc123\"", "\"abc123\""));
        Assert.True(ReadEtagProvider.Matches("W/\"abc123\"", "\"abc123\""));
        Assert.True(ReadEtagProvider.Matches("*", "\"abc123\""));
        Assert.True(ReadEtagProvider.Matches("\"other\", \"abc123\"", "\"abc123\""));
    }

    [Fact]
    // ⚠ US-710 / U9-CON-2 — the reason registrant detail carries no ETag at all.
    //
    // Athlete profile events are appended with the ATHLETE id as their scope, not the event id, so
    // editing an athlete's weight does NOT move the event watermark. This test demonstrates the
    // hazard directly: after a real weight change, the event-scoped watermark is unchanged, so an
    // ETag built from it would still match a client's stale If-None-Match and return 304 with the
    // old weight. The endpoint is therefore excluded from conditional handling (Q1=A).
    public async Task Profile_edit_does_not_move_the_event_watermark_so_detail_is_uncached()
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, organizer) = await h.SeedOpenEventAsync();
        long parent = h.Ids.NextId();
        var registrationId = await h.RegisterAsync(parent, eventId, divisionId, weight: 70);

        var athleteId = h.Db.RegistrationRows.Single(r => r.RegistrationId == registrationId).AthleteId;
        var watermarkBefore = await h.Etags.WatermarkAsync(eventId);

        await h.Registrations.UpsertProfileAsync(parent, athleteId, new ProfileInput(
            "Athlete", new DateOnly(2001, 1, 1), 5, 68, "Academy B", "M"));

        var watermarkAfter = await h.Etags.WatermarkAsync(eventId);
        var detail = await h.RegistrantQueries.GetAsync(eventId, registrationId, AccessTier.Organizer, organizer);

        Assert.Equal(watermarkBefore, watermarkAfter);   // the gap: watermark blind to the profile edit
        Assert.Equal(68, detail.Value.Weight);            // the fresh read is correct
        // Because the watermark cannot detect this change, EventReadController.GetRegistrant
        // deliberately sets no ETag and never returns 304 — see BR-READ-26.
    }
}
