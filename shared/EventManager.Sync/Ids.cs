using IdGen;

namespace EventManager.Sync;

/// <summary>Thrown when the system clock regresses beyond the tolerated wait window (Q8 alarm).</summary>
public sealed class ClockRegressionException : Exception
{
    public ClockRegressionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Generates 64-bit time-sortable Snowflake ids (D-26). Thread-safe (IdGen).</summary>
public interface IIdGenerator
{
    long NextId();
}

/// <summary>
/// Adapter over IdGen (TSD-3, P-2): IdStructure(41,10,12), epoch 2026-01-01, SpinWait on per-tick
/// sequence exhaustion. On clock regression IdGen throws; we wait up to a bound for the clock to
/// catch up (Q8), then raise <see cref="ClockRegressionException"/> as an alarm.
/// </summary>
public sealed class SnowflakeIdGenerator : IIdGenerator
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IdGen.IdGenerator _generator;
    private readonly TimeSpan _maxRegressionWait;

    public SnowflakeIdGenerator(int workerId, ITimeSource? timeSource = null, TimeSpan? maxRegressionWait = null)
    {
        var options = new IdGeneratorOptions(
            new IdStructure(41, 10, 12),
            timeSource ?? new DefaultTimeSource(Epoch),
            SequenceOverflowStrategy.SpinWait);
        _generator = new IdGen.IdGenerator(workerId, options);
        _maxRegressionWait = maxRegressionWait ?? TimeSpan.FromMilliseconds(500);
    }

    public long NextId()
    {
        var deadline = DateTime.UtcNow + _maxRegressionWait;
        while (true)
        {
            try
            {
                return _generator.CreateId();
            }
            catch (InvalidSystemClockException ex)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new ClockRegressionException("Clock regressed beyond the tolerated wait window.", ex);
                Thread.Sleep(1); // let the wall clock catch up
            }
        }
    }
}

/// <summary>
/// Assigns unique Snowflake worker ids within an event scope (Q10). The hub is the authority;
/// worker id 0 is reserved (e.g., for the cloud). Assignments are event-scoped.
/// </summary>
public interface IWorkerIdRegistry
{
    int Assign(long deviceId);
    void Release(long deviceId);
    int? WorkerIdFor(long deviceId);
}

public sealed class WorkerIdRegistry : IWorkerIdRegistry
{
    private readonly Dictionary<long, int> _assigned = new();
    private readonly SortedSet<int> _free;
    private readonly object _gate = new();

    public WorkerIdRegistry(int firstWorkerId = 1, int maxWorkerId = 1023)
    {
        _free = new SortedSet<int>();
        for (int w = firstWorkerId; w <= maxWorkerId; w++) _free.Add(w);
    }

    public int Assign(long deviceId)
    {
        lock (_gate)
        {
            if (_assigned.TryGetValue(deviceId, out var existing)) return existing;
            if (_free.Count == 0) throw new InvalidOperationException("No free worker ids in this event scope.");
            var w = _free.Min;
            _free.Remove(w);
            _assigned[deviceId] = w;
            return w;
        }
    }

    public void Release(long deviceId)
    {
        lock (_gate)
        {
            if (_assigned.Remove(deviceId, out var w)) _free.Add(w);
        }
    }

    public int? WorkerIdFor(long deviceId)
    {
        lock (_gate) return _assigned.TryGetValue(deviceId, out var w) ? w : null;
    }
}
