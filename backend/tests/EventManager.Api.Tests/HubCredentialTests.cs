using EventManager.Api.Auth;
using EventManager.Api.Services;
using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.Api.Tests;

/// <summary>
/// U10 hub-credential lifecycle and ingest authorization (US-801, US-808, US-809).
/// Rules under test: BR-REPL-1..8, 10, 13..15, 19..21.
/// </summary>
public sealed class HubCredentialTests
{
    // ---------------- Issuance ----------------

    [Fact]
    public async Task Issue_returns_the_key_once_and_stores_only_a_hash() // BR-REPL-2, BR-REPL-3
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();

        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");

        Assert.False(issued.IsError);
        Assert.False(string.IsNullOrWhiteSpace(issued.Value.Key));

        var stored = h.Db.HubCredentials.Single();
        Assert.NotEqual(issued.Value.Key, stored.KeyHash);          // never stored in the clear
        Assert.DoesNotContain(issued.Value.Key, stored.KeyHash);

        // The listing shape carries no key material at all.
        var list = await h.HubCredentials.ListAsync(admin, eventId);
        Assert.False(list.IsError);
        Assert.Single(list.Value);
        Assert.Equal("Active", list.Value[0].State);
    }

    [Fact]
    public async Task Issue_requires_organizer_rights_on_the_event() // BR-REPL-1
    {
        using var h = new TestHost();
        var (eventId, _, _, _) = await h.SeedOpenEventAsync();

        var issued = await h.HubCredentials.IssueAsync(issuerAccountId: 999999, eventId, "someone else's hub");

        Assert.True(issued.IsError);
    }

    [Fact]
    public async Task Issue_requires_a_label() // BR-REPL-6
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();

        Assert.True((await h.HubCredentials.IssueAsync(admin, eventId, "")).IsError);
        Assert.True((await h.HubCredentials.IssueAsync(admin, eventId, new string('x', 121))).IsError);
    }

    [Fact]
    public async Task Expiry_is_the_event_date_plus_grace_and_is_never_caller_supplied() // BR-REPL-4
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();   // event date 2026-09-01

        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");

        Assert.Equal(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), issued.Value.ExpiresAt);
    }

    [Fact]
    public async Task At_most_three_active_credentials_per_event_and_expired_ones_free_a_slot() // BR-REPL-5
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();

        for (var i = 0; i < 3; i++)
            Assert.False((await h.HubCredentials.IssueAsync(admin, eventId, $"hub {i}")).IsError);

        Assert.True((await h.HubCredentials.IssueAsync(admin, eventId, "hub 4")).IsError);   // cap reached

        // Expired credentials do not occupy a slot, so a long-running event cannot become un-issuable.
        h.Clock.Now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.False((await h.HubCredentials.IssueAsync(admin, eventId, "hub 4")).IsError);
    }

    // ---------------- Authentication ----------------

    [Fact]
    public async Task A_valid_key_authenticates_to_its_own_event_scope() // BR-REPL-7
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");

        var principal = await h.HubCredentials.AuthenticateAsync(issued.Value.Key);

        Assert.NotNull(principal);
        Assert.Equal(eventId, principal!.EventScopeId);
        Assert.Equal(issued.Value.CredentialId, principal.CredentialId);
    }

    [Fact]
    public async Task Unknown_revoked_and_expired_keys_are_indistinguishable() // BR-REPL-7, BR-REPL-14
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        var revoked = await h.HubCredentials.IssueAsync(admin, eventId, "revoked hub");
        var expiring = await h.HubCredentials.IssueAsync(admin, eventId, "expiring hub");

        await h.HubCredentials.RevokeAsync(admin, eventId, revoked.Value.CredentialId);

        Assert.Null(await h.HubCredentials.AuthenticateAsync("not-a-real-key"));
        Assert.Null(await h.HubCredentials.AuthenticateAsync(revoked.Value.Key));

        h.Clock.Now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(await h.HubCredentials.AuthenticateAsync(expiring.Value.Key));   // expired ≡ revoked
    }

    [Fact]
    public async Task Revocation_takes_effect_on_the_very_next_request() // BR-REPL-8, BR-REPL-15
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");

        Assert.NotNull(await h.HubCredentials.AuthenticateAsync(issued.Value.Key));
        await h.HubCredentials.RevokeAsync(admin, eventId, issued.Value.CredentialId);
        Assert.Null(await h.HubCredentials.AuthenticateAsync(issued.Value.Key));     // no session, no cache
    }

    [Fact]
    public async Task Revoking_twice_is_not_an_error() // BR-REPL-15
    {
        using var h = new TestHost();
        var (eventId, _, _, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");

        Assert.False((await h.HubCredentials.RevokeAsync(admin, eventId, issued.Value.CredentialId)).IsError);
        Assert.False((await h.HubCredentials.RevokeAsync(admin, eventId, issued.Value.CredentialId)).IsError);
    }

    // ---------------- Ingest authorization and provenance ----------------

    [Fact]
    public async Task A_hub_credential_may_ingest_for_its_own_event() // BR-REPL-10
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");
        var caller = new IngestCaller.Hub(issued.Value.CredentialId, eventId);

        var result = await h.Ingest.IngestAsync(caller, Batch(eventId, athleteId, divisionId, 3));

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.AcceptedCount);
    }

    [Fact]
    public async Task A_credential_for_another_event_is_refused_and_stores_nothing() // BR-REPL-10
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, _) = await h.SeedOpenEventAsync();
        var caller = new IngestCaller.Hub(CredentialId: 4242, EventScopeId: eventId + 1);

        var result = await h.Ingest.IngestAsync(caller, Batch(eventId, athleteId, divisionId, 3));

        Assert.True(result.IsError);
        Assert.Empty(h.Db.Events.Where(e => e.DeviceId == 7));
    }

    [Fact]
    public async Task A_batch_spanning_a_foreign_scope_is_refused_ENTIRELY() // BR-REPL-10 — no partial acceptance
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");
        var caller = new IngestCaller.Hub(issued.Value.CredentialId, eventId);

        var mixed = new ReplicationBatchDto(
        [
            .. Batch(eventId, athleteId, divisionId, 2).Events,
            .. Batch(eventId + 999, athleteId, divisionId, 1, startSeq: 3).Events,
        ]);

        var result = await h.Ingest.IngestAsync(caller, mixed);

        Assert.True(result.IsError);
        Assert.Empty(h.Db.Events.Where(e => e.DeviceId == 7));   // the in-scope half is NOT kept
    }

    [Fact]
    public async Task Provenance_records_the_first_deliverer_and_replay_does_not_rewrite_it() // BR-REPL-19, BR-REPL-20
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, admin) = await h.SeedOpenEventAsync();
        var first = await h.HubCredentials.IssueAsync(admin, eventId, "original hub");
        var replacement = await h.HubCredentials.IssueAsync(admin, eventId, "replacement hub");
        var batch = Batch(eventId, athleteId, divisionId, 2);

        await h.Ingest.IngestAsync(new IngestCaller.Hub(first.Value.CredentialId, eventId), batch);
        var second = await h.Ingest.IngestAsync(new IngestCaller.Hub(replacement.Value.CredentialId, eventId), batch);

        Assert.Equal(0, second.Value.AcceptedCount);   // idempotent
        foreach (var row in h.Db.Events.Where(e => e.DeviceId == 7))
            Assert.Equal(first.Value.CredentialId, row.IngestedByCredentialId);   // NOT the most recent sender
    }

    [Fact]
    public async Task Cloud_authored_events_carry_no_provenance() // BR-REPL-21
    {
        using var h = new TestHost();
        await h.SeedOpenEventAsync();   // seeding writes through EventWriter, not ingest

        Assert.All(h.Db.Events.ToList(), e => Assert.Null(e.IngestedByCredentialId));
    }

    [Fact]
    public async Task Account_based_ingest_still_works_unchanged() // BR-REPL-13
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, admin) = await h.SeedOpenEventAsync();

        var result = await h.Ingest.IngestAsync(new IngestCaller.Account(admin), Batch(eventId, athleteId, divisionId, 2));

        Assert.False(result.IsError);
        Assert.All(h.Db.Events.Where(e => e.DeviceId == 7).ToList(), e => Assert.Null(e.IngestedByCredentialId));
    }

    [Fact]
    public async Task High_water_marks_are_scoped_to_the_credentials_event() // BR-REPL-11
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, admin) = await h.SeedOpenEventAsync();
        var issued = await h.HubCredentials.IssueAsync(admin, eventId, "main hub");
        var caller = new IngestCaller.Hub(issued.Value.CredentialId, eventId);
        await h.Ingest.IngestAsync(caller, Batch(eventId, athleteId, divisionId, 4));

        var own = await h.Ingest.HighWaterMarksAsync(caller, eventId);
        var foreign = await h.Ingest.HighWaterMarksAsync(caller, eventId + 1);

        Assert.False(own.IsError);
        Assert.Equal(4, own.Value[7]);
        Assert.True(foreign.IsError);
    }

    // ---------------- Helpers ----------------

    private static ReplicationBatchDto Batch(long eventId, long athleteId, long divisionId, int count, long startSeq = 1)
    {
        var events = new List<EventEnvelopeDto>();
        for (var i = 0; i < count; i++)
        {
            var seq = startSeq + i;
            events.Add(new EventEnvelopeDto(
                EventId: 1_000_000 + seq, DeviceId: 7, SequenceNumber: seq,
                EventType: "CheckedIn", SchemaVersion: 1,
                PayloadBase64: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    $"{{\"athleteId\":{athleteId},\"divisionId\":{divisionId}}}")),
                OccurredAt: new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero).AddMinutes(i),
                EventScopeId: eventId));
        }
        return new ReplicationBatchDto(events);
    }
}
