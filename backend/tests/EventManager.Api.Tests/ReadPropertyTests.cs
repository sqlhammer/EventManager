using EventManager.Api.Auth;
using EventManager.Api.Contracts;
using EventManager.Api.Persistence;
using EventManager.Domain;
using FsCheck.Xunit;

namespace EventManager.Api.Tests;

/// <summary>
/// Property-based tests for the U9 read surface — properties P1..P6 from
/// `functional-design/business-logic-model.md` §7 (PBT-01..PBT-10).
///
/// These complement, never replace, the example-based tests in ReadTierTests / ReadShapeTests /
/// ReadNonDisclosureTests / ReadEtagTests (PBT-10). Assertion style matches RbacTests.
/// </summary>
public sealed class ReadPropertyTests
{
    /// <summary>Domain generator for a registration status — a constrained domain value rather than
    /// a raw primitive (PBT-07).</summary>
    private static RegistrationStatusRow StatusFrom(int seed)
    {
        var values = Enum.GetValues<RegistrationStatusRow>();
        return values[Math.Abs(seed % values.Length)];
    }

    private static AccessTier TierFrom(int seed)
    {
        var tiers = new[] { AccessTier.Public, AccessTier.Registrant, AccessTier.Organizer };
        return tiers[Math.Abs(seed % tiers.Length)];
    }

    [Property(MaxTest = 50)]
    // P1 — Deny by default (invariant). A caller with no organizer role and no registration gets
    // Public on an open event and None otherwise, and a None tier always yields an error.
    public void No_relationship_means_no_access(int statusSeed, int callerSeed)
    {
        var status = StatusFrom(statusSeed);
        using var h = new TestHost();
        var (eventId, _, _, organizer) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        if (status != RegistrationStatusRow.Open)
            h.Events.SetRegistrationOpenAsync(organizer, eventId, false).GetAwaiter().GetResult();

        long stranger = Math.Abs((long)callerSeed) + 1_000_000;   // never an organizer or registrant
        var tier = h.ReadAuth.ResolveAsync(stranger, eventId).GetAwaiter().GetResult();

        if (status == RegistrationStatusRow.Open)
        {
            Assert.Equal(AccessTier.Public, tier);
        }
        else
        {
            Assert.Equal(AccessTier.None, tier);
            Assert.True(h.EventQueries.GetAsync(eventId, tier).GetAwaiter().GetResult().IsError);
        }
    }

    [Property(MaxTest = 50)]
    // P1 (continued) — no-tier callers are denied on every endpoint, for any generated event state.
    public void No_tier_is_denied_on_every_endpoint(int registrationSeed)
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        long parent = h.Ids.NextId();
        var registrationId = h.RegisterAsync(parent, eventId, divisionId,
            name: $"Athlete {Math.Abs(registrationSeed % 500)}").GetAwaiter().GetResult();

        Assert.True(h.EventQueries.GetAsync(eventId, AccessTier.None).GetAwaiter().GetResult().IsError);
        Assert.True(h.DivisionQueries.ListAsync(eventId, AccessTier.None, false).GetAwaiter().GetResult().IsError);
        Assert.True(h.DivisionQueries.GetAsync(eventId, divisionId, AccessTier.None).GetAwaiter().GetResult().IsError);
        Assert.True(h.PolicyQueries.GetAsync(eventId, AccessTier.None).GetAwaiter().GetResult().IsError);
        Assert.True(h.RegistrantQueries.ListAsync(eventId, AccessTier.None, false).GetAwaiter().GetResult().IsError);
        Assert.True(h.RegistrantQueries.GetAsync(eventId, registrationId, AccessTier.None, parent).GetAwaiter().GetResult().IsError);
        Assert.True(h.AccountQueries.ListAsync(eventId, AccessTier.None).GetAwaiter().GetResult().IsError);
    }

    [Property(MaxTest = 50)]
    // P2 — Shape confinement (invariant). A Public response is always the summary shape and never
    // the detail shape, for any generated event configuration.
    public void Public_tier_never_receives_detail_fields(bool cardEnabled, bool closeRegistration)
    {
        using var h = new TestHost();
        var (eventId, _, _, organizer) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        if (cardEnabled) h.Events.SetPaymentOptionsAsync(organizer, eventId, true).GetAwaiter().GetResult();
        if (closeRegistration) h.Events.SetRegistrationOpenAsync(organizer, eventId, false).GetAwaiter().GetResult();

        var body = h.EventQueries.GetAsync(eventId, AccessTier.Public).GetAwaiter().GetResult().Value;

        Assert.IsType<EventSummaryResponse>(body);
        Assert.IsNotType<EventDetailResponse>(body);
    }

    [Property(MaxTest = 40)]
    // P3 — Query equivalence (oracle). The division query equals a naive in-memory filter over the
    // same rows, for any generated completion pattern and flag combination.
    public void Division_query_matches_a_naive_filter(bool markComplete, bool includeCompleted)
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, _) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        if (markComplete)
        {
            var row = h.Db.DivisionRows.Find(divisionId)!;
            row.Status = nameof(DivisionStatus.Complete);
            h.Db.SaveChanges();
        }

        var actual = h.DivisionQueries.ListAsync(eventId, AccessTier.Public, includeCompleted)
            .GetAwaiter().GetResult().Value.Select(d => d.DivisionId).OrderBy(id => id).ToList();

        var oracle = h.Db.DivisionRows.AsEnumerable()
            .Where(d => d.EventId == eventId)
            .Where(d => includeCompleted || d.Status != nameof(DivisionStatus.Complete))
            .Select(d => d.DivisionId).OrderBy(id => id).ToList();

        Assert.Equal(oracle, actual);
    }

    [Property(MaxTest = 40)]
    // P4 — Conditional stability (idempotence). Repeating an identical read while the watermark is
    // unchanged returns an identical body and an identical ETag, at any tier.
    public void Repeated_reads_are_stable_while_the_watermark_holds(int tierSeed)
    {
        var tier = TierFrom(tierSeed);
        using var h = new TestHost();
        var (eventId, _, _, _) = h.SeedOpenEventAsync().GetAwaiter().GetResult();

        var watermark = h.Etags.WatermarkAsync(eventId).GetAwaiter().GetResult();
        var first = h.EventQueries.GetAsync(eventId, tier).GetAwaiter().GetResult().Value;
        var firstTag = h.Etags.Build("event", eventId, watermark, tier);
        var second = h.EventQueries.GetAsync(eventId, tier).GetAwaiter().GetResult().Value;
        var secondTag = h.Etags.Build("event", eventId, watermark, tier);

        Assert.Equal(first, second);
        Assert.Equal(firstTag, secondTag);
    }

    [Property(MaxTest = 40)]
    // P4 (continued) — a tier change must change the ETag even at an unchanged watermark, so a
    // caller who gains a tier can never be served a 304 over their stale narrower body.
    public void Etag_is_tier_sensitive_at_a_fixed_watermark(int seedA, int seedB)
    {
        var tierA = TierFrom(seedA);
        var tierB = TierFrom(seedB);
        using var h = new TestHost();
        var (eventId, _, _, _) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        var watermark = h.Etags.WatermarkAsync(eventId).GetAwaiter().GetResult();

        var tagA = h.Etags.Build("event", eventId, watermark, tierA);
        var tagB = h.Etags.Build("event", eventId, watermark, tierB);

        if (tierA == tierB) Assert.Equal(tagA, tagB);
        else Assert.NotEqual(tagA, tagB);
    }

    [Property(MaxTest = 40)]
    // P5 — Tier monotonicity (invariant). Gaining a registration or an organizer role never lowers
    // the resolved tier.
    public void Gaining_a_relationship_never_lowers_the_tier(bool register, bool promote)
    {
        using var h = new TestHost();
        var (eventId, divisionId, _, admin) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        long caller = h.Ids.NextId();

        var before = h.ReadAuth.ResolveAsync(caller, eventId).GetAwaiter().GetResult();
        if (register) h.RegisterAsync(caller, eventId, divisionId).GetAwaiter().GetResult();
        if (promote) h.OrganizerRoles.AddExistingAsync(admin, eventId, caller).GetAwaiter().GetResult();
        var after = h.ReadAuth.ResolveAsync(caller, eventId).GetAwaiter().GetResult();

        Assert.True(after >= before, $"tier regressed: {before} -> {after}");
    }

    [Property(MaxTest = 40)]
    // P6 — Cross-scope isolation (invariant). An id belonging to another event is never readable
    // under this event's path, at any tier.
    public void Cross_event_ids_are_never_readable(int tierSeed)
    {
        var tier = TierFrom(tierSeed);
        using var h = new TestHost();
        var (eventA, _, _, organizerA) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        var (eventB, divisionB, _, _) = h.SeedOpenEventAsync().GetAwaiter().GetResult();
        long parent = h.Ids.NextId();
        var registrationB = h.RegisterAsync(parent, eventB, divisionB).GetAwaiter().GetResult();

        Assert.True(h.DivisionQueries.GetAsync(eventA, divisionB, tier).GetAwaiter().GetResult().IsError);
        Assert.True(h.RegistrantQueries.GetAsync(eventA, registrationB, tier, organizerA).GetAwaiter().GetResult().IsError);
    }
}
