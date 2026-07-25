using System.Collections.Concurrent;
using EventManager.Sync;

namespace EventManager.Judge.Core;

/// <summary>
/// On-device event store used by the app core and tests. The production MAUI host swaps in a
/// SQLite/SQLCipher-backed <see cref="IEventStore"/> (deferred seam); the durable-before-ack contract
/// is identical either way (idempotent <see cref="AppendIfNotExistsAsync"/>).
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<(long Device, long Seq), TournamentEvent> _byKey = new();

    public Task<bool> AppendIfNotExistsAsync(TournamentEvent evt, CancellationToken ct = default)
    {
        var added = _byKey.TryAdd((evt.DeviceId, evt.SequenceNumber), evt);
        return Task.FromResult(added);
    }

    public Task<IReadOnlyList<TournamentEvent>> ReadStreamAsync(long deviceId, long fromSequenceExclusive, CancellationToken ct = default)
    {
        IReadOnlyList<TournamentEvent> stream = _byKey.Values
            .Where(e => e.DeviceId == deviceId && e.SequenceNumber > fromSequenceExclusive)
            .OrderBy(e => e.SequenceNumber).ToList();
        return Task.FromResult(stream);
    }

    public Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct = default)
    {
        var seqs = _byKey.Values.Where(e => e.DeviceId == deviceId).Select(e => e.SequenceNumber).OrderBy(s => s).ToList();
        long hwm = 0;
        foreach (var s in seqs)
        {
            if (s == hwm + 1) hwm = s;
            else if (s > hwm + 1) break;
        }
        return Task.FromResult(hwm);
    }

    public async IAsyncEnumerable<TournamentEvent> ReadAllAsync(long? fromEventIdExclusive = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var ordered = _byKey.Values.OrderBy(e => e.EventId).AsEnumerable();
        if (fromEventIdExclusive is { } from) ordered = ordered.Where(e => e.EventId > from);
        foreach (var e in ordered) yield return e;
        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListDeviceIdsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<long> ids = _byKey.Values.Select(e => e.DeviceId).Distinct().ToList();
        return Task.FromResult(ids);
    }
}
