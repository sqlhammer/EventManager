using EventManager.Api.Events;
using EventManager.Api.Auth;
using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.Api.Tests;

/// <summary>PBT-4 + example: ingest idempotency (BR-ING-2) and event-scoped authz (BR-ING-1).</summary>
public sealed class IngestServiceTests
{
    private static ReplicationBatchDto Batch(long scopeId, long athleteId, long divisionId, int count, long hubDevice = 7)
    {
        var ser = new JsonEventSerializer();
        var events = new List<EventEnvelopeDto>();
        for (int i = 1; i <= count; i++)
        {
            var payload = ser.Serialize(new MatchCompletedPayload(athleteId, scopeId, divisionId, Won: i % 2 == 0));
            var te = new TournamentEvent(EventId: 1000 + i, DeviceId: hubDevice, SequenceNumber: i,
                EventType: EventTypes.MatchCompleted, SchemaVersion: 1, Payload: payload,
                OccurredAt: DateTimeOffset.UtcNow, EventScopeId: scopeId);
            events.Add(EventEnvelopeMapper.ToDto(te));
        }
        return new ReplicationBatchDto(events);
    }

    [Fact]
    public async Task Ingesting_same_batch_twice_is_idempotent() // PBT-4 (example form)
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, adminAccount) = await h.SeedOpenEventAsync();
        var batch = Batch(eventId, athleteId, divisionId, count: 4);

        var first = await h.Ingest.IngestAsync(new IngestCaller.Account(adminAccount), batch);
        var second = await h.Ingest.IngestAsync(new IngestCaller.Account(adminAccount), batch);

        Assert.False(first.IsError);
        Assert.Equal(4, first.Value.AcceptedCount);
        Assert.Equal(0, second.Value.AcceptedCount);              // replay accepts nothing
        Assert.Equal(4, h.Db.Events.Count(e => e.DeviceId == 7)); // no duplicates
    }

    [Fact]
    public async Task Ingest_for_unauthorized_scope_is_rejected() // BR-ING-1
    {
        using var h = new TestHost();
        var (eventId, divisionId, athleteId, _) = await h.SeedOpenEventAsync();
        var batch = Batch(eventId, athleteId, divisionId, count: 2);

        var result = await h.Ingest.IngestAsync(new IngestCaller.Account(999999), batch); // not an organizer on the scope
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Ingest_out_of_order_batch_folds_deterministically() // PBT-4 order-independence
    {
        using var h1 = new TestHost();
        using var h2 = new TestHost();
        var s1 = await h1.SeedOpenEventAsync();
        var s2 = await h2.SeedOpenEventAsync();

        var forward = Batch(s1.EventId, s1.AthleteId, s1.DivisionId, 4);
        var reversed = new ReplicationBatchDto(Batch(s2.EventId, s2.AthleteId, s2.DivisionId, 4).Events.Reverse().ToList());

        await h1.Ingest.IngestAsync(new IngestCaller.Account(s1.AccountId), forward);
        await h2.Ingest.IngestAsync(new IngestCaller.Account(s2.AccountId), reversed);

        Assert.Equal(h1.Db.Events.Count(e => e.DeviceId == 7), h2.Db.Events.Count(e => e.DeviceId == 7));
    }
}
