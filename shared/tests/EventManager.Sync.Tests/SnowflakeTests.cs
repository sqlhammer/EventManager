using EventManager.Sync;
using Xunit;

namespace EventManager.Sync.Tests;

public class SnowflakeTests
{
    [Fact] // BR-2.1/2.2 monotonic per worker + unique under burst
    public void Ids_AreMonotonicAndUnique()
    {
        var gen = new SnowflakeIdGenerator(workerId: 1);
        var ids = new List<long>(20000);
        for (int i = 0; i < 20000; i++) ids.Add(gen.NextId());

        for (int i = 1; i < ids.Count; i++)
            Assert.True(ids[i] > ids[i - 1], $"id at {i} not greater than previous");
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact] // BR-2.3 clock regression beyond the wait bound raises an alarm
    public void Ids_ClockRegressionBeyondBound_Throws()
    {
        var ts = new MutableTimeSource(SnowflakeIdGenerator.Epoch, startTicks: 1000);
        var gen = new SnowflakeIdGenerator(workerId: 1, timeSource: ts, maxRegressionWait: TimeSpan.FromMilliseconds(5));

        gen.NextId();          // establishes last-generated tick at 1000
        ts.Set(500);           // clock moves backwards and stays there

        Assert.Throws<ClockRegressionException>(() => gen.NextId());
    }

    [Fact] // distinct workers never collide even at the same instant
    public void Ids_DifferentWorkers_DoNotCollide()
    {
        var a = new SnowflakeIdGenerator(workerId: 1);
        var b = new SnowflakeIdGenerator(workerId: 2);
        var ids = new HashSet<long>();
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(ids.Add(a.NextId()));
            Assert.True(ids.Add(b.NextId()));
        }
    }
}
