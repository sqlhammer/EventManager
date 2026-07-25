using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.Hub.Resilience;

public sealed record ReplicationResult(bool Attempted, int EventsReplicated);
public sealed record CompletenessReport(bool IsComplete, long LocalEventCount, long ReplicatedEventCount);

/// <summary>
/// Drives hub→cloud replication (US-504) and verifies post-event completeness (US-602). Uses the U1
/// <see cref="IReplicationProtocol"/> to compute the next batch above each device's cloud high-water
/// mark, sends via <see cref="ICloudReplicationTransport"/> with bounded retry/backoff, and advances the
/// cursors from the ack — so an outage is a no-op that resumes gap-free, and re-runs never duplicate.
/// </summary>
public sealed class ReplicationClient(
    IEventStore local, IReplicationProtocol protocol, ICloudReplicationTransport transport,
    int maxBatch = 500, int maxAttempts = 3)
{
    private readonly Dictionary<long, long> _cloudHighWaterMarks = new();

    public async Task<ReplicationResult> ReplicateAsync(CancellationToken ct = default)
    {
        if (!transport.IsOnline) return new ReplicationResult(Attempted: false, EventsReplicated: 0);

        var replicated = 0;
        while (true)
        {
            var batch = await protocol.NextBatchAsync(local, _cloudHighWaterMarks, maxBatch, ct);
            if (batch.Count == 0) break;

            var progressedBefore = SnapshotCursorSum();
            var dto = new ReplicationBatchDto(batch.Select(EventEnvelopeMapper.ToDto).ToList());
            var ack = await SendWithRetryAsync(dto, ct);

            foreach (var (deviceId, hwm) in ack.PerDeviceHighWaterMarks)
            {
                var current = _cloudHighWaterMarks.GetValueOrDefault(deviceId, 0);
                _cloudHighWaterMarks[deviceId] = Math.Max(current, hwm);
            }
            replicated += ack.AcceptedCount;

            // Safety: if the cursor did not advance, stop to avoid an infinite loop.
            if (SnapshotCursorSum() == progressedBefore) break;
        }
        return new ReplicationResult(Attempted: true, EventsReplicated: replicated);
    }

    /// <summary>Post-event completeness (US-602): every local event is mirrored to the cloud.</summary>
    public async Task<CompletenessReport> VerifyCompletenessAsync(CancellationToken ct = default)
    {
        long localCount = 0;
        long replicatedCount = 0;
        foreach (var deviceId in await local.ListDeviceIdsAsync(ct))
        {
            var localHwm = await local.HighWaterMarkAsync(deviceId, ct);
            localCount += localHwm;
            replicatedCount += Math.Min(localHwm, _cloudHighWaterMarks.GetValueOrDefault(deviceId, 0));
        }
        return new CompletenessReport(localCount == replicatedCount, localCount, replicatedCount);
    }

    private async Task<ReplicationAckDto> SendWithRetryAsync(ReplicationBatchDto dto, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await transport.SendAsync(dto, ct);
            }
            catch when (attempt < maxAttempts)
            {
                var backoff = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
                await Task.Delay(backoff, ct);
            }
        }
    }

    private long SnapshotCursorSum()
    {
        long sum = 0;
        foreach (var v in _cloudHighWaterMarks.Values) sum += v;
        return sum;
    }
}
