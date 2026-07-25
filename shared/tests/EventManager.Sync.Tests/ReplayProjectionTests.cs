using EventManager.Sync;
using FsCheck.Xunit;
using Xunit;

namespace EventManager.Sync.Tests;

public class ReplayProjectionTests
{
    /// <summary>Order-sensitive projection: appends EventId; ProjectionHost sorts by EventId on Rebuild.</summary>
    private sealed class EventIdListProjection : IProjection<IReadOnlyList<long>>
    {
        public IReadOnlyList<long> Empty => Array.Empty<long>();
        public IReadOnlyList<long> Apply(IReadOnlyList<long> state, TournamentEvent evt)
            => new List<long>(state) { evt.EventId };
    }

    [Fact] // BR-1.2 idempotent append
    public async Task Append_IsIdempotentOnEventId()
    {
        var store = new InMemoryEventStore();
        var e = Events.Make(eventId: 5, deviceId: 1, seq: 1);

        Assert.True(await store.AppendIfNotExistsAsync(e));
        Assert.False(await store.AppendIfNotExistsAsync(e)); // replay -> no-op

        var all = new List<TournamentEvent>();
        await foreach (var x in store.ReadAllAsync()) all.Add(x);
        Assert.Single(all);
    }

    [Fact] // BR-1.3 replay fold is idempotent (duplicates ignored)
    public void ReplayFold_IgnoresDuplicates()
    {
        var engine = new ReplayEngine();
        var events = new[]
        {
            Events.Make(1, 1, 1), Events.Make(2, 1, 2), Events.Make(2, 1, 2), // dup EventId 2
        };
        var count = engine.Fold(0, events, (acc, _) => acc + 1);
        Assert.Equal(2, count);
    }

    [Property] // BR-1.4 projection state is deterministic regardless of arrival order
    public void Projection_RebuildIsOrderIndependent(int[]? raw)
    {
        var ids = (raw ?? Array.Empty<int>())
            .Select(x => (long)Math.Abs(x))
            .Where(x => x > 0)
            .Distinct()
            .Take(50)
            .ToList();
        if (ids.Count == 0) return;

        var events = ids.Select(id => Events.Make(id, 1, id)).ToList();

        var host1 = new ProjectionHost<IReadOnlyList<long>>(new EventIdListProjection());
        var host2 = new ProjectionHost<IReadOnlyList<long>>(new EventIdListProjection());

        var s1 = host1.Rebuild(events);
        var s2 = host2.Rebuild(Enumerable.Reverse(events).ToList());

        Assert.Equal(s1, s2); // both sorted by EventId
    }

    [Fact] // BR-1.3 dispatching the same event twice is a no-op
    public void Projection_DispatchDuplicate_IsNoOp()
    {
        var host = new ProjectionHost<IReadOnlyList<long>>(new EventIdListProjection());
        var e = Events.Make(7, 1, 1);
        host.Dispatch(e);
        host.Dispatch(e);
        Assert.Single(host.State);
    }
}
