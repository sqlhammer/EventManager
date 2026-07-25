using EventManager.Sync;
using Xunit;

namespace EventManager.Sync.Tests;

public class ReplicationTests
{
    private static async Task<InMemoryEventStore> StoreWith(params TournamentEvent[] events)
    {
        var s = new InMemoryEventStore();
        foreach (var e in events) await s.AppendIfNotExistsAsync(e);
        return s;
    }

    [Fact] // US-504 next batch = events above each peer high-water mark, in sequence order
    public async Task NextBatch_ReturnsEventsAbovePeerHighWaterMark()
    {
        var store = await StoreWith(
            Events.Make(11, 1, 1), Events.Make(12, 1, 2), Events.Make(13, 1, 3),
            Events.Make(21, 2, 1), Events.Make(22, 2, 2));

        var protocol = new ReplicationProtocol();
        var peer = new Dictionary<long, long> { [1] = 2 }; // device 1 already has up to seq 2

        var batch = await protocol.NextBatchAsync(store, peer, maxBatch: 100);

        Assert.Contains(batch, e => e.DeviceId == 1 && e.SequenceNumber == 3);
        Assert.DoesNotContain(batch, e => e.DeviceId == 1 && e.SequenceNumber <= 2);
        Assert.Equal(2, batch.Count(e => e.DeviceId == 2)); // device 2 fully behind
    }

    [Fact]
    public async Task NextBatch_RespectsMaxBatch()
    {
        var store = await StoreWith(
            Events.Make(1, 1, 1), Events.Make(2, 1, 2), Events.Make(3, 1, 3), Events.Make(4, 1, 4));

        var batch = await new ReplicationProtocol().NextBatchAsync(store, new Dictionary<long, long>(), maxBatch: 2);
        Assert.Equal(2, batch.Count);
    }

    [Fact] // BR-1.5 gap detection
    public async Task DetectGaps_FindsMissingSequences()
    {
        var store = await StoreWith(
            Events.Make(1, 1, 1), Events.Make(2, 1, 2), Events.Make(4, 1, 4)); // missing seq 3

        var gaps = await new ReplicationProtocol().DetectGapsAsync(store, deviceId: 1);
        Assert.Single(gaps);
        Assert.Equal(new SeqRange(1, 3, 3), gaps[0]);
    }

    [Fact]
    public async Task HighWaterMark_IsLastContiguousSequence()
    {
        var store = await StoreWith(
            Events.Make(1, 1, 1), Events.Make(2, 1, 2), Events.Make(4, 1, 4));
        Assert.Equal(2, await store.HighWaterMarkAsync(1)); // stops before the gap at 3
    }
}
