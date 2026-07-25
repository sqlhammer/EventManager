using EventManager.ClientSync;
using EventManager.Judge.Core;
using EventManager.Sync;

namespace EventManager.Judge.Core.Tests;

/// <summary>U5 judge app-core tests: durable-before-ack scoring, capture, queue, cross-mat read-only, focus.</summary>
public sealed class JudgeCoreTests
{
    private const long JudgeDeviceId = 88;
    private const long EventScope = 1;

    private static (ScoreCaptureService Capture, InMemoryEventStore Store, LocalEventQueue Queue) NewJudge()
    {
        var store = new InMemoryEventStore();
        var log = new SpokeEventLog(store, new SnowflakeIdGenerator(workerId: 2), new JsonEventSerializer(), JudgeDeviceId);
        return (new ScoreCaptureService(log), store, new LocalEventQueue(store));
    }

    [Fact]
    public async Task Point_sparring_score_is_durable_before_ack() // US-402 / NFR-1.1
    {
        var (capture, store, queue) = NewJudge();
        var evt = await capture.CapturePointSparringAsync(EventScope,
            new PointSparringScorePayload(10, 500, CompetitorA: 1, PointsA: 5, PenaltiesA: 0, CompetitorB: 2, PointsB: 3, PenaltiesB: 0));

        // Persisted before the call returned: it is already in the local store / pending queue.
        Assert.Equal(JudgeEventTypes.PointSparringScored, evt.EventType);
        Assert.Equal(1, await store.HighWaterMarkAsync(JudgeDeviceId));
        Assert.Single(await queue.PendingAsync());
    }

    [Fact]
    public async Task Sequences_are_contiguous_per_device() // gap-free stream
    {
        var (capture, store, _) = NewJudge();
        await capture.CapturePointSparringAsync(EventScope, new PointSparringScorePayload(10, 1, 1, 1, 0, 2, 0, 0));
        await capture.CaptureFormsAsync(EventScope, new FormsScorePayload(10, 2, [new FormsCompetitorScore(1, [9.1, 9.2])]));
        Assert.Equal(2, await store.HighWaterMarkAsync(JudgeDeviceId));   // contiguous 1,2
    }

    [Fact]
    public async Task Queued_scores_drain_after_hub_ack() // sync loop
    {
        var (capture, _, queue) = NewJudge();
        await capture.CapturePointSparringAsync(EventScope, new PointSparringScorePayload(10, 1, 1, 1, 0, 2, 0, 0));
        Assert.Equal(1, await queue.QueuedCountAsync());
        queue.MarkAcked(new Dictionary<long, long> { [JudgeDeviceId] = 1 });
        Assert.Equal(0, await queue.QueuedCountAsync());
    }

    [Fact]
    public void Mat_queue_advances_on_completion() // US-401
    {
        var vm = new MatQueueViewModel();
        vm.Replace([new QueuedMatch(10, 100, 1, 2), new QueuedMatch(10, 101, 3, 4)]);
        Assert.Equal(100, vm.Current!.MatchId);
        vm.CompleteCurrent();
        Assert.Equal(101, vm.Current!.MatchId);
        vm.CompleteCurrent();
        Assert.Null(vm.Current);
    }

    [Fact]
    public void Cross_mat_view_is_read_only() // US-410
    {
        var vm = new CrossMatViewModel();
        vm.Update(matDivisionId: 20, [new QueuedMatch(20, 200, 5, 6)]);
        Assert.Single(vm.ForMat(20));
        Assert.Empty(vm.ForMat(99));
        // No write/enqueue method exists on the cross-mat view — enforced by type, not runtime.
        Assert.DoesNotContain(typeof(CrossMatViewModel).GetMethods(), m => m.Name.Contains("Enqueue") || m.Name.Contains("Capture"));
    }

    [Fact]
    public void Focus_mode_locks_and_unlocks() // US-411
    {
        var focus = new FocusModeState();
        Assert.False(focus.IsLocked);
        focus.Lock(500);
        Assert.True(focus.IsLocked);
        Assert.Equal(500, focus.LockedMatchId);
        focus.Unlock();
        Assert.False(focus.IsLocked);
        Assert.Null(focus.LockedMatchId);
    }
}
