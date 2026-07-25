using IdGen;

namespace EventManager.Sync.Tests;

/// <summary>Controllable <see cref="ITimeSource"/> for Snowflake regression tests.</summary>
internal sealed class MutableTimeSource : ITimeSource
{
    private long _ticks;

    public MutableTimeSource(DateTimeOffset epoch, long startTicks = 0)
    {
        Epoch = epoch;
        _ticks = startTicks;
    }

    public DateTimeOffset Epoch { get; }
    public TimeSpan TickDuration { get; } = TimeSpan.FromMilliseconds(1);

    public long GetTicks() => Interlocked.Read(ref _ticks);

    public void Set(long ticks) => Interlocked.Exchange(ref _ticks, ticks);
}
