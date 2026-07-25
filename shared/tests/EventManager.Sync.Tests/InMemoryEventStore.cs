using System.Runtime.CompilerServices;
using EventManager.Sync;

namespace EventManager.Sync.Tests;

/// <summary>Test-double <see cref="IEventStore"/> (single-writer, in-memory) for U1 tests.</summary>
internal sealed class InMemoryEventStore : IEventStore
{
    private readonly List<TournamentEvent> _events = new();
    private readonly HashSet<long> _ids = new();
    private readonly object _gate = new();

    public Task<bool> AppendIfNotExistsAsync(TournamentEvent evt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_ids.Add(evt.EventId)) return Task.FromResult(false);
            _events.Add(evt);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<TournamentEvent>> ReadStreamAsync(long deviceId, long fromSequenceExclusive, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TournamentEvent> result = _events
                .Where(e => e.DeviceId == deviceId && e.SequenceNumber > fromSequenceExclusive)
                .OrderBy(e => e.SequenceNumber)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var seqs = _events.Where(e => e.DeviceId == deviceId).Select(e => e.SequenceNumber).OrderBy(s => s);
            long hwm = 0;
            foreach (var s in seqs)
            {
                if (s == hwm + 1) hwm = s;
                else if (s > hwm + 1) break;
            }
            return Task.FromResult(hwm);
        }
    }

    public async IAsyncEnumerable<TournamentEvent> ReadAllAsync(long? fromEventIdExclusive = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<TournamentEvent> snapshot;
        lock (_gate)
        {
            snapshot = _events
                .Where(e => fromEventIdExclusive is null || e.EventId > fromEventIdExclusive)
                .OrderBy(e => e.EventId)
                .ToList();
        }
        foreach (var e in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListDeviceIdsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<long> ids = _events.Select(e => e.DeviceId).Distinct().OrderBy(x => x).ToList();
            return Task.FromResult(ids);
        }
    }
}

internal static class Events
{
    public static TournamentEvent Make(long eventId, long deviceId, long seq) =>
        new(eventId, deviceId, seq, "Test", 1, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch, 42);
}
