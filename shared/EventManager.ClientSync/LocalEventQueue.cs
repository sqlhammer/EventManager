using EventManager.Sync;

namespace EventManager.ClientSync;

/// <summary>
/// Durable outbox over U1's <see cref="IEventStore"/> (Q2=A). Events are persisted before ack
/// (BR-CS-1); acked items are tracked by per-device high-water mark and are prune-eligible.
/// </summary>
public sealed class LocalEventQueue
{
    private readonly IEventStore _store;
    private readonly Dictionary<long, long> _ackedHwm = new(); // deviceId -> highest acked sequence
    private readonly object _gate = new();

    public LocalEventQueue(IEventStore store) => _store = store;

    /// <summary>Persist durably BEFORE returning (the caller may ack the UI only after this).</summary>
    public Task EnqueueDurableAsync(TournamentEvent evt, CancellationToken ct = default)
        => _store.AppendIfNotExistsAsync(evt, ct);

    /// <summary>Events not yet acked, in (device, sequence) order (BR-CS-8).</summary>
    public async Task<IReadOnlyList<TournamentEvent>> PendingAsync(CancellationToken ct = default)
    {
        var pending = new List<TournamentEvent>();
        await foreach (var e in _store.ReadAllAsync(null, ct))
        {
            long acked;
            lock (_gate) acked = _ackedHwm.GetValueOrDefault(e.DeviceId, 0);
            if (e.SequenceNumber > acked) pending.Add(e);
        }
        return pending.OrderBy(e => e.DeviceId).ThenBy(e => e.SequenceNumber).ToList();
    }

    public void MarkAcked(IReadOnlyDictionary<long, long> perDeviceHighWaterMarks)
    {
        lock (_gate)
            foreach (var (device, hwm) in perDeviceHighWaterMarks)
                _ackedHwm[device] = Math.Max(_ackedHwm.GetValueOrDefault(device, 0), hwm);
    }

    public async Task<int> QueuedCountAsync(CancellationToken ct = default)
        => (await PendingAsync(ct)).Count;

    public long LastAckedSequence()
    {
        lock (_gate) return _ackedHwm.Count == 0 ? 0 : _ackedHwm.Values.Max();
    }
}
