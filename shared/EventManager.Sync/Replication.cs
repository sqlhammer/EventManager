namespace EventManager.Sync;

/// <summary>A contiguous missing range in a device stream.</summary>
public readonly record struct SeqRange(long DeviceId, long FromInclusive, long ToInclusive);

/// <summary>Per-device replication progress (high-water mark).</summary>
public sealed record ReplicationCursor(long DeviceId, long LastAckedSequence);

/// <summary>
/// Sequence-ordered replication (P-10, FR-4.6, US-504). Computes the next batch above each peer's
/// high-water mark and detects gaps. Owned here as logic; U7 wires it cross-cutting (hub↔cloud).
/// </summary>
public interface IReplicationProtocol
{
    Task<IReadOnlyList<TournamentEvent>> NextBatchAsync(
        IEventStore store, IReadOnlyDictionary<long, long> peerHighWaterMarks, int maxBatch, CancellationToken ct = default);

    Task<IReadOnlyList<SeqRange>> DetectGapsAsync(IEventStore store, long deviceId, CancellationToken ct = default);
}

public sealed class ReplicationProtocol : IReplicationProtocol
{
    public async Task<IReadOnlyList<TournamentEvent>> NextBatchAsync(
        IEventStore store, IReadOnlyDictionary<long, long> peerHighWaterMarks, int maxBatch, CancellationToken ct = default)
    {
        var batch = new List<TournamentEvent>();
        foreach (var deviceId in await store.ListDeviceIdsAsync(ct))
        {
            long from = peerHighWaterMarks.TryGetValue(deviceId, out var hwm) ? hwm : 0;
            var pending = await store.ReadStreamAsync(deviceId, from, ct);
            foreach (var e in pending.OrderBy(e => e.SequenceNumber))
            {
                batch.Add(e);
                if (batch.Count >= maxBatch)
                    return batch.OrderBy(e => e.SequenceNumber).ToList();
            }
        }
        return batch;
    }

    public async Task<IReadOnlyList<SeqRange>> DetectGapsAsync(IEventStore store, long deviceId, CancellationToken ct = default)
    {
        var events = await store.ReadStreamAsync(deviceId, 0, ct);
        var seqs = events.Select(e => e.SequenceNumber).OrderBy(s => s).ToList();
        var gaps = new List<SeqRange>();
        if (seqs.Count == 0) return gaps;

        // Streams are expected contiguous from 1; report any hole.
        long expected = 1;
        foreach (var s in seqs)
        {
            if (s > expected) gaps.Add(new SeqRange(deviceId, expected, s - 1));
            expected = Math.Max(expected, s + 1);
        }
        return gaps;
    }
}
