using EventManager.Checkin.Core;
using EventManager.ClientSync;
using EventManager.Domain;
using EventManager.Domain.Engines;
using EventManager.Sync;

namespace EventManager.Checkin.Core.Tests;

/// <summary>U6 check-in app-core tests: durable check-in, weigh-in range validation + recommendation.</summary>
public sealed class CheckinCoreTests
{
    private const long DeviceId = 91;
    private const long EventScope = 1;

    private static SpokeEventLog NewLog(InMemoryEventStore store) =>
        new(store, new SnowflakeIdGenerator(workerId: 3), new JsonEventSerializer(), DeviceId);

    private static Division Div(double upper) => new((Snowflake)10, (Snowflake)1,
        new DivisionCriteria(new WeightClass(null, upper), new RankRange(0, 100), new AgeRange(0, 120), "M"),
        BracketFormat.SingleElimination, DivisionStatus.NotStarted);

    [Fact]
    public async Task Check_in_is_durable_before_ack() // US-306 / NFR-1.1
    {
        var store = new InMemoryEventStore();
        var service = new CheckInService(NewLog(store));
        var evt = await service.CheckInAsync(EventScope, athleteId: 500);

        Assert.Equal(CheckinEventTypes.AthleteCheckedIn, evt.EventType);
        Assert.Equal(1, await store.HighWaterMarkAsync(DeviceId));
        Assert.Single(await new LocalEventQueue(store).PendingAsync());
    }

    [Fact]
    public async Task In_range_weigh_in_is_green_and_recorded() // US-307
    {
        var store = new InMemoryEventStore();
        var service = new WeighInService(NewLog(store), new WeighInPolicyEvaluator());
        var feedback = await service.RecordAsync(EventScope, athleteId: 500, weight: 65,
            Div(upper: 70), new WeighInPolicy(WeighInPolicyMode.Strict), []);

        Assert.True(feedback.InRange);
        Assert.Equal(WeighInResult.Pass, feedback.Result);
        Assert.Equal(1, await store.HighWaterMarkAsync(DeviceId));   // recorded as immutable history
    }

    [Fact]
    public async Task Out_of_range_weigh_in_is_flagged() // US-307 routes to policy flow
    {
        var store = new InMemoryEventStore();
        var service = new WeighInService(NewLog(store), new WeighInPolicyEvaluator());
        var feedback = await service.RecordAsync(EventScope, athleteId: 500, weight: 80,
            Div(upper: 70), new WeighInPolicy(WeighInPolicyMode.Strict), []);

        Assert.False(feedback.InRange);
        Assert.Equal(WeighInResult.Disqualified, feedback.Result);
    }

    [Fact]
    public async Task Staff_can_attach_a_nonbinding_recommendation() // D-25
    {
        var store = new InMemoryEventStore();
        var log = NewLog(store);
        var service = new WeighInService(log, new WeighInPolicyEvaluator());
        await service.RecordAsync(EventScope, 500, 80, Div(70), new WeighInPolicy(WeighInPolicyMode.Strict), [],
            recommendedResolution: WeighInPolicyMode.AutoMove);

        var recorded = (await store.ReadStreamAsync(DeviceId, 0)).Single();
        var payload = new JsonEventSerializer().Deserialize<WeighInRecordedPayload>(recorded.Payload);
        Assert.Equal(nameof(WeighInPolicyMode.AutoMove), payload.RecommendedResolution);
    }

    [Fact]
    public async Task Corrections_are_new_events_not_mutations() // US-307 immutable history
    {
        var store = new InMemoryEventStore();
        var service = new WeighInService(NewLog(store), new WeighInPolicyEvaluator());
        await service.RecordAsync(EventScope, 500, 80, Div(70), new WeighInPolicy(WeighInPolicyMode.Strict), []);
        await service.RecordAsync(EventScope, 500, 69, Div(70), new WeighInPolicy(WeighInPolicyMode.Strict), []); // correction

        Assert.Equal(2, await store.HighWaterMarkAsync(DeviceId));   // two events, nothing overwritten
    }
}
